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
    GatewayCallers callers) : ControllerBase
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
        // Lowered on the column too: rows saved before names had to be lowercase may not be.
        var apiGateway = await dbContext.Set<ApiGateway>()
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

        var validatorProperties = subscription.ValidatorProperties.ToDictionary()
            .Fill(partner, globalAdapterValuesSet);
        await xchangeService.RunValidator(subscription.ValidatorId, validatorProperties,
            xchangeFile);

        var xchangeReferences = new List<string> { callerReference };
        var globalAdapterValuesSets = await dbContext.Set<GlobalAdapterValuesSet>().ToArrayAsync();
        var xchangeId = await xchangeService.SubmitSubscriptionXchange(subscription.Id, xchangeFile,
            xchangeReferences.ToArray(), partner, globalAdapterValuesSets);
        if (!resultSync)
        {
            return Accepted(xchangeId);
        }

        var waitResponse = 120;
        //  check headers for wait response value
        var waitResponseHeader = Request.Headers["Wait-Period"].FirstOrDefault();
        if (int.TryParse(waitResponseHeader, out var waitResponseValue))
        {
            waitResponse = waitResponseValue <= 0 ? 120 : waitResponseValue;
        }

        var currentFibTerm = 1;
        var previousTerm = 1;
        while (currentFibTerm <= waitResponse)
        {
            await Task.Delay(TimeSpan.FromSeconds(currentFibTerm));
            var nextTerm = Math.Min(currentFibTerm + previousTerm, 8);
            previousTerm = currentFibTerm;
            currentFibTerm = nextTerm;
            if (!await dbContext.Set<XchangeResult>()
                    .AsNoTracking()
                    .AnyAsync(i => i.Id == xchangeId)) continue;

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