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
[Route("api/[controller]")]
public class GatewayController(
    BitweenDbContext dbContext,
    RequestContext requestContext,
    IInfolinkCache cache,
    XchangeService xchangeService,
    BitweenOptions bitweenSettings) : ControllerBase
{
    [HttpPost("{gatewayApiName}/sync")]
    public Task<IActionResult> PostSync([FromRoute] string gatewayApiName)
    {
        return ProcessAsync(gatewayApiName, resultSync: true);
    }

    [HttpPost("{gatewayApiName}/async")]
    public Task<IActionResult> PostAsync([FromRoute] string gatewayApiName)
    {
        return ProcessAsync(gatewayApiName, resultSync: false);
    }

    private async Task<IActionResult> ProcessAsync([FromRoute] string gatewayApiName, bool resultSync)
    {
        var globalAdapterValuesSet = await cache.ListGlobalAdapterValuesSetsAsync();
        var apiGateway = await dbContext.Set<ApiGateway>()
            .Include(ag => ag.Partners)
            .ThenInclude(agp => agp.Partner)
            .FirstOrDefaultAsync(ag => ag.UrlName == gatewayApiName);

        if (apiGateway == null)
            return NotFound();

        // Resolve partner using API key
        var (authorized, partner, keyName) = await dbContext.CheckPartnerAuthorized(requestContext);

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

        var xchangeFile = new XchangeFile(json, $"{gatewayApiName}.json");

        var validatorProperties = subscription.ValidatorProperties.ToDictionary()
            .Fill(partner, globalAdapterValuesSet);
        await xchangeService.RunValidator(subscription.ValidatorId, validatorProperties,
            xchangeFile);

        var xchangeReferences = new List<string> { $"partnerkey: {keyName}" };
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