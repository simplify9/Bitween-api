using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading.Tasks;
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
    public  Task<IActionResult> PostSync([FromRoute] string gatewayApiName)
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
            throw new SWNotFoundException($"API Gateway with URL name '{gatewayApiName}' not found");

        // Resolve partner using API key
        var (partner, keyName) = await dbContext.AuthorizePartner(requestContext);

        // Verify partner is part of the API Gateway
        var apiGatewayPartner = apiGateway.Partners.FirstOrDefault(agp => agp.PartnerId == partner.Id);
        if (apiGatewayPartner == null)
            throw new SWUnauthorizedException("Partner is not authorized for this API Gateway");

        var subscription = await cache.SubscriptionByIdAsync(apiGatewayPartner.SubscriptionId);
        

        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

        var xchangeFile = new XchangeFile(json);
        
        var validatorProperties = subscription.ValidatorProperties.ToDictionary()
            .Fill(partner, globalAdapterValuesSet);
        await xchangeService.RunValidator(subscription.ValidatorId, validatorProperties,
            xchangeFile);

        var xchangeReferences = new List<string> { $"partnerkey: {keyName}" };
        var xchangeId= await xchangeService.SubmitSubscriptionXchange(subscription.Id, xchangeFile, xchangeReferences.ToArray());
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
                    throw new SWValidationException("failure", "Internal processing error.");
            }
        }

        return Accepted(xchangeId);
    }
    
}