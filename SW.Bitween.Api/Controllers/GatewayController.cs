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
    GatewayActivity activity,
    BitweenOptions options,
    Services.Metrics.MetricsRecorder metrics,
    ResultSignals signals) : ControllerBase
{
    /// <summary>A sync call's wait for its own exchange's result, from before it is published to the answer.</summary>
    private ResultSignals.Wait signal;

    /// <summary>Answers a call whose payload the type dropped as a duplicate, naming the exchange it repeats.</summary>
    public const string DuplicateOfHeader = "Bitween-Duplicate-Of";

    /// <summary>
    /// <c>{gateway's url name}/sync</c> or <c>/async</c>. The url name can be several segments,
    /// so it is everything before the last one - a route can't put a literal after a catch-all.
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
        // Creation and the sync wait, counted together; see DbCommandTally.
        var commands = Services.Metrics.DbCommandTally.Open("gateway");
        try
        {
            return await ProcessCallAsync(gatewayApiName, resultSync);
        }
        finally
        {
            signal?.Dispose();
            commands.Record(metrics);
        }
    }

    private async Task<IActionResult> ProcessCallAsync(string gatewayApiName, bool resultSync)
    {
        using var created = BitweenTelemetry.Stage("gateway.create");
        var globalAdapterValuesSet = await cache.ListGlobalAdapterValuesSetsAsync();
        // From the cache, with its attachments and partners, as the subscriptions are: these were two
        // queries on every call. A save to a gateway, an attachment, a partner or a key revokes it.
        var apiGateway = await cache.ApiGatewayByUrlNameAsync(gatewayApiName);

        var address = HttpContext.Connection.RemoteIpAddress?.ToString();
        if (apiGateway == null)
        {
            activity.Refused(gatewayApiName, "unknown-gateway", 404, address);
            return NotFound();
        }

        // Resolve the partner the way this gateway asks callers to prove who they are
        var (authorized, partner, callerReference, keyName) = await callers.Identify(apiGateway, requestContext);

        if (!authorized)
        {
            activity.Refused(gatewayApiName, "not-authenticated", 401, address);
            return Unauthorized();
        }

        // Verify partner is part of the API Gateway
        var apiGatewayPartner = apiGateway.Partners.FirstOrDefault(agp => agp.PartnerId == partner.Id);
        if (apiGatewayPartner == null)
        {
            activity.Refused(gatewayApiName, "not-attached", 401, address);
            return Unauthorized();
        }
        await activity.KeyUsedAsync(partner.Id, keyName);
        Services.Metrics.DbCommandTally.Current?.AttributeTo(DateTime.UtcNow, apiGatewayPartner.SubscriptionId, partner.Id);

        // After authorisation on purpose: whether a gateway exists and is switched off is
        // something only an attached partner should learn - checking it earlier would
        // answer that for anyone who guessed the url.
        //
        // 503 rather than 404 because the url is right and the partner should keep it: a
        // 404 reads as "wrong address" and sends someone hunting for a new one, where this
        // is a gateway somebody switched off and will switch back on.
        if (apiGateway.Inactive)
        {
            activity.Refused(gatewayApiName, "gateway-off", 503, address);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                $"The '{apiGateway.Name}' gateway is currently deactivated.");
        }

        var subscription = await cache.SubscriptionByIdAsync(apiGatewayPartner.SubscriptionId);

        if (subscription == null)
        {
            activity.Refused(gatewayApiName, "no-subscription", 404, address);
            return NotFound();
        }

        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

        // The file name travels with the exchange into handlers, some of which write it to disk -
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

        // The caller may ask for its own wait in Wait-Period; it is capped, see ResultWait.
        var waitResponse = ResultWait.Clamp(
            int.TryParse(Request.Headers["Wait-Period"].FirstOrDefault(), out var asked) ? asked : null,
            options.MaxResponseWaitSeconds);

        // A repeat of a call the partner couldn't be sure arrived - same Idempotency-Key - gets the
        // exchange the first one made, not a second. Scoped to this gateway and this partner.
        var idempotencyKey = Request.Headers[IdempotencyGuard.Header].FirstOrDefault()?.Trim();
        if (idempotencyKey?.Length > IdempotencyGuard.MaxKeyLength)
            return BadRequest($"{IdempotencyGuard.Header} is longer than {IdempotencyGuard.MaxKeyLength} characters.");
        var idempotencyScope = $"gateway:{apiGateway.Id}:partner:{partner.Id}";

        var xchangeId = string.IsNullOrEmpty(idempotencyKey)
            ? null
            : await IdempotencyGuard.FindAsync(dbContext, idempotencyScope, idempotencyKey);
        // Not for a replayed call: its payload is a repeat by definition, and it is answered already.
        var duplicate = xchangeId is null
            ? await new DuplicateArrivals(dbContext).CheckAsync(document, partner.Id, xchangeFile)
            : null;

        if (xchangeId is not null)
        {
            Response.Headers["Idempotent-Replay"] = "true";
        }
        else if (duplicate?.Drop == true)
        {
            // The same payload from this partner within the type's duplicate window, which the type
            // drops: answered with the exchange it repeats, as a replayed call would be.
            await dbContext.SaveChangesAsync();
            xchangeId = duplicate.OriginalXchangeId;
            Response.Headers[DuplicateOfHeader] = xchangeId;
        }
        else
        {
            if (duplicate != null && duplicate.FitsWith(xchangeReferences)) xchangeReferences.Add(duplicate.Reference);

            // The sets already read at the top of the request, from the cache.
            var xchange = await xchangeService.CreateXchange(subscription, xchangeFile, xchangeReferences.ToArray(),
                Guid.NewGuid().ToString("N"), partner, globalAdapterValuesSet);
            if (resultSync)
            {
                // Before the save publishes the exchange, so its result can't arrive unannounced. The
                // created message names this node, and the worker rings the bell here once the result
                // is saved.
                signal = signals.Register(xchange.Id, TimeSpan.FromSeconds(waitResponse + 5));
                dbContext.Handoff.AwaitedOn(xchange.Id, signals.Node);
            }

            if (string.IsNullOrEmpty(idempotencyKey))
            {
                await dbContext.SaveChangesAsync();
                xchangeId = xchange.Id;
            }
            else
            {
                await IdempotencyGuard.StageAsync(dbContext, idempotencyScope, idempotencyKey, xchange.Id);
                (xchangeId, var replayed) =
                    await IdempotencyGuard.SaveAsync(dbContext, idempotencyScope, idempotencyKey, xchange.Id);
                if (replayed) Response.Headers["Idempotent-Replay"] = "true";
            }

            // Answered with an earlier call's exchange instead: this one was never published.
            if (xchangeId != xchange.Id)
            {
                signal?.Dispose();
                signal = null;
            }
        }

        created.Dispose();
        if (!resultSync)
        {
            return Accepted(xchangeId);
        }

        using var waited = BitweenTelemetry.Stage("gateway.wait");

        // Each check reads the result itself rather than asking whether there is one and then reading
        // it: one query once it is there, where it was two.
        async Task<XchangeResultReady> ReadResultAsync() => XchangeResultReady.From(
            await dbContext.Set<XchangeResult>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == xchangeId));

        XchangeResultReady ready;
        if (signal != null)
        {
            // The worker's signal, with the database asked only as a backstop: a signal lost to a
            // restart, or a worker on a version that sends none.
            ready = await ResultWait.UntilAsync(signal.Result, ReadResultAsync, waitResponse, HttpContext.RequestAborted);
        }
        else
        {
            // A replayed or dropped call, answered with an exchange this call didn't create: usually
            // finished already, so asked at once, then polled.
            ready = await ReadResultAsync();
            if (ready == null)
                await ResultWait.UntilAsync(async () => (ready = await ReadResultAsync()) != null,
                    waitResponse, HttpContext.RequestAborted);
        }

        if (ready != null)
        {
            switch (ready.Success)
            {
                case true when ready.ResponseSize == 0:
                    {
                        return Ok(xchangeId);
                    }
                case true when ready.ResponseSize != 0:
                    {
                        // From the signal when it brought the response; from storage when it was too
                        // large to travel, or the backstop found the result.
                        var response = ready.Response ?? await xchangeService.GetFile(xchangeId, XchangeFileType.Response);
                        return new ContentResult
                        {
                            StatusCode = ready.ResponseBad ? 400 : 200,
                            Content = response,
                            ContentType = ready.ResponseContentType ?? MediaTypeNames.Application.Json,
                        };
                    }
                case false:
                {
                    if (apiGateway.FailureAnswer == GatewayFailureAnswer.Legacy) return BadRequest();
                    // The retry, if any, is scheduled in the same save as the result, so it is there now.
                    var retryOn = await dbContext.Set<DelayedRetry>().AsNoTracking()
                        .Where(r => r.Id == xchangeId).Select(r => (DateTime?)r.On).FirstOrDefaultAsync();
                    var hasMapper = await dbContext.Set<Xchange>().AsNoTracking()
                        .Where(x => x.Id == xchangeId).Select(x => x.MapperId != null).FirstOrDefaultAsync();
                    var answer = GatewayFailureAnswers.For(apiGateway.FailureAnswer, xchangeId, hasMapper,
                        mapped: ready.OutputSize > 0 || ready.OutputName != null, retryOn);
                    return new ObjectResult(answer.Body) { StatusCode = answer.Status };
                }
            }
        }

        return Accepted(xchangeId);
    }
}