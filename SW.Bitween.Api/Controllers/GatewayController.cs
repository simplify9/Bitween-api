using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Controllers;

[ApiController]
[Route("api/gateway")]
public class GatewayController(
    BitweenDbContext dbContext,
    RequestContext requestContext,
    IInfolinkCache cache,
    XchangeService xchangeService,
    GatewayCallers callers,
    BitweenOptions options) : ControllerBase
{
    /// <summary>
    /// <c>{gateway's url name}/sync</c> or <c>/async</c>. The url name can be several segments,
    /// so it is everything before the last one — a route can't put a literal after a catch-all.
    /// </summary>
    /// <remarks>
    /// The literal "gateway" is what keeps this clear of CqApi, which owns the rest of /api/ with
    /// templates up to three segments deep: without it, /api/logistics/slim/sync is an admin call.
    /// </remarks>
    [HttpPost("{**path}")]
    public Task<IActionResult> Post([FromRoute] string path)
    {
        var trimmed = (path ?? "").Trim('/').ToLowerInvariant();
        var split = trimmed.LastIndexOf('/');
        if (split <= 0)
            return Task.FromResult<IActionResult>(NotFound());

        return trimmed[(split + 1)..] switch
        {
            "sync" => ProcessAsync(trimmed[..split], resultSync: true),
            "async" => ProcessAsync(trimmed[..split], resultSync: false),
            _ => Task.FromResult<IActionResult>(NotFound()),
        };
    }

    private async Task<IActionResult> ProcessAsync(string gatewayApiName, bool resultSync)
    {
        var globalAdapterValuesSet = await cache.ListGlobalAdapterValuesSetsAsync();
        // The exact name first, which the unique index on UrlName answers. Lowering the column for
        // every call made each one a scan of the table; it is kept as the fallback only, for rows
        // saved before names had to be lowercase.
        var apiGateway = await dbContext.Set<ApiGateway>()
            .Include(ag => ag.Partners)
            .ThenInclude(agp => agp.Partner)
            .FirstOrDefaultAsync(ag => ag.UrlName == gatewayApiName)
            ?? await dbContext.Set<ApiGateway>()
                .Include(ag => ag.Partners)
                .ThenInclude(agp => agp.Partner)
                .FirstOrDefaultAsync(ag => ag.UrlName.ToLower() == gatewayApiName);

        if (apiGateway == null)
            return NotFound();

        // Resolve the partner the way this gateway asks callers to prove who they are
        var (authorized, partner, callerReference) = await callers.Identify(apiGateway, requestContext);

        if (!authorized)
            return Unauthorized();

        // Verify partner is part of the API Gateway
        var apiGatewayPartner = apiGateway.Partners.FirstOrDefault(agp => agp.PartnerId == partner.Id);
        if (apiGatewayPartner == null)
            return Unauthorized();

        // After authorisation on purpose: whether a gateway exists and is switched off is
        // something only an attached partner should learn — checking it earlier would
        // answer that for anyone who guessed the url.
        //
        // 503 rather than 404 because the url is right and the partner should keep it: a
        // 404 reads as "wrong address" and sends someone hunting for a new one, where this
        // is a gateway somebody switched off and will switch back on.
        if (apiGateway.Inactive)
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                $"The '{apiGateway.Name}' gateway is currently deactivated.");

        var subscription = await cache.SubscriptionByIdAsync(apiGatewayPartner.SubscriptionId);

        if (subscription == null)
            return NotFound();

        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

        // The file name travels with the exchange into handlers, some of which write it to disk —
        // a slash there is a directory nobody asked for. JSON and XML types keep the ".json"
        // they have always had, since whatever picks those files up may expect it.
        var document = await cache.DocumentByIdAsync(subscription.DocumentId);
        var extension = document?.DocumentFormat switch
        {
            DocumentFormat.Csv => ".csv",
            DocumentFormat.Other => "",
            _ => ".json",
        };
        var xchangeFile = new XchangeFile(json, $"{gatewayApiName.Replace('/', '-')}{extension}");

        // Told now, while the partner is still on the line, rather than as a failed exchange later.
        var schemaErrors = document is null
            ? []
            : await DocumentSchema.Check(document.DocumentFormat, document.ValidationSchema, json);
        if (schemaErrors.Count > 0)
            return BadRequest(new { error = "SCHEMA_MISMATCH", errors = schemaErrors });

        var validatorProperties = subscription.ValidatorProperties.ToDictionary()
            .Fill(partner, globalAdapterValuesSet);
        await xchangeService.RunValidator(subscription.ValidatorRef, validatorProperties,
            xchangeFile);

        var xchangeReferences = new List<string> { callerReference };

        // A repeat of a call the partner couldn't be sure arrived — same Idempotency-Key — gets the
        // exchange the first one made, not a second. Scoped to this gateway and this partner.
        var idempotencyKey = Request.Headers[IdempotencyGuard.Header].FirstOrDefault()?.Trim();
        if (idempotencyKey?.Length > IdempotencyGuard.MaxKeyLength)
            return BadRequest($"{IdempotencyGuard.Header} is longer than {IdempotencyGuard.MaxKeyLength} characters.");
        var idempotencyScope = $"gateway:{apiGateway.Id}:partner:{partner.Id}";

        var xchangeId = string.IsNullOrEmpty(idempotencyKey)
            ? null
            : await IdempotencyGuard.FindAsync(dbContext, idempotencyScope, idempotencyKey);
        if (xchangeId is not null)
        {
            Response.Headers["Idempotent-Replay"] = "true";
        }
        else
        {
            // The sets already read at the top of the request, from the cache.
            var xchange = await xchangeService.CreateXchange(subscription, xchangeFile, xchangeReferences.ToArray(),
                Guid.NewGuid().ToString("N"), partner, globalAdapterValuesSet);
            if (!string.IsNullOrEmpty(idempotencyKey))
                await IdempotencyGuard.StageAsync(dbContext, idempotencyScope, idempotencyKey, xchange.Id);

            try
            {
                await dbContext.SaveChangesAsync();
                xchangeId = xchange.Id;
            }
            catch (DbUpdateException ex) when (IdempotencyGuard.IsDuplicateKey(ex))
            {
                // Another call with the same key committed first: its exchange is the answer.
                dbContext.ChangeTracker.Clear();
                xchangeId = await IdempotencyGuard.FindAsync(dbContext, idempotencyScope, idempotencyKey);
                Response.Headers["Idempotent-Replay"] = "true";
            }
        }

        if (!resultSync)
        {
            return Accepted(xchangeId);
        }

        // The caller may ask for its own wait in Wait-Period; it is capped, see ResultWait.
        var waitResponse = ResultWait.Clamp(
            int.TryParse(Request.Headers["Wait-Period"].FirstOrDefault(), out var asked) ? asked : null,
            options.MaxResponseWaitSeconds);

        var available = await ResultWait.UntilAsync(
            () => dbContext.Set<XchangeResult>().AsNoTracking().AnyAsync(i => i.Id == xchangeId),
            waitResponse, HttpContext.RequestAborted);

        if (available)
        {
            var xchangeResult = await dbContext.FindAsync<XchangeResult>(xchangeId);


            switch (xchangeResult!.Success)
            {
                case true when xchangeResult.ResponseSize == 0:
                    {
                        return Ok(xchangeId);
                    }
                case true when xchangeResult.ResponseSize != 0:
                    {
                        var response = await xchangeService.GetFile(xchangeId, XchangeFileType.Response);
                        return new ContentResult
                        {
                            StatusCode = xchangeResult.ResponseBad ? 400 : 200,
                            Content = response,
                            ContentType = xchangeResult.ResponseContentType ?? MediaTypeNames.Application.Json,
                        };
                    }
                case false:
                    return BadRequest();
            }
        }

        return Accepted(xchangeId);
    }
}