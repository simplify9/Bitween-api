using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SW.Bitween.Resources.Xchanges
{
    [Unprotect]
    public class Update(RequestContext requestContext, XchangeService xchangeService, BitweenDbContext dbContext,
        BitweenOptions BitweenSettings, IInfolinkCache cache) : ICommandHandler<string, object,object>
    {
        public async Task<object> Handle(string documentIdOrName, dynamic request)
        {
            Document document;

            //Inject external request context values into the object
            request._ExternalRequestContext = JsonConvert.SerializeObject(requestContext.Values);

            if (int.TryParse(documentIdOrName, out var documentId))
                document = await cache.DocumentByIdAsync(documentId);
            else
                document = await cache.DocumentByNameAsync(documentIdOrName);

            if (document is null)
                throw new SWNotFoundException("Document");

            var par = await dbContext.AuthorizePartner(requestContext);

            var subs = (await cache.ListSubscriptionsByDocumentAsync(document.Id))
                .Where(i => i.PartnerId == par.Partner.Id)
                .ToList();

            if (subs?.Count > 1)
                throw new SWValidationException("Subscriptions",
                    "You can only have one subscription of for each document");

            var sub = subs.SingleOrDefault();

            if (par.Partner.Id == Partner.SystemId && sub is null)
            {
                await xchangeService.SubmitFilterXchange(document.Id,new XchangeFile(request.ToString()));
                return null;
            }

            if (sub is null)
                throw new SWNotFoundException("No subscription of type ApiCall was found for this document");

            var xchangeReferences = new List<string> { $"partnerkey: {par.KeyName}" };

            var waitResponseHeader = requestContext.Values
                .Where(item => item.Name.ToLower() == "waitresponse")
                .Select(item => item.Value).FirstOrDefault();

            var waitResponse = 0;
            if (int.TryParse(waitResponseHeader, out var waitResponseValue))
            {
                waitResponse = waitResponseValue <= 0 ? 120 : waitResponseValue;
                xchangeReferences.Add($"waitresponse: {waitResponse}");
            }

            var xchangeFile = new XchangeFile(request.ToString());

            var globalAdapterValuesSets = await cache.ListGlobalAdapterValuesSetsAsync();
            var validatorProperties = sub.ValidatorProperties.ToDictionary().Fill(par.Partner, globalAdapterValuesSets);
            await xchangeService.RunValidator(sub.ValidatorId, validatorProperties, xchangeFile);

            var xchangeId =
                await xchangeService.SubmitSubscriptionXchange(sub.Id, xchangeFile, xchangeReferences.ToArray());

            if (waitResponse <= 0)
                return new CqApiResult<string>(xchangeId)
                {
                    Status = CqApiResultStatus.Ok
                };

            var currentFibTerm = 1;
            var previousTerm = 1;
            while (currentFibTerm <= waitResponse)
            {
                await Task.Delay(TimeSpan.FromSeconds(currentFibTerm));
                var nextTerm = Math.Min(currentFibTerm + previousTerm, 8);
                previousTerm = currentFibTerm;
                currentFibTerm = nextTerm;
                if (!await IsResultAvailable(xchangeId)) continue;

                var xchangeResult = await dbContext.FindAsync<XchangeResult>(xchangeId);

                switch (xchangeResult!.Success)
                {
                    case true when xchangeResult.ResponseSize == 0:
                    {
                        // The partner's own choice, falling back to the instance-wide setting
                        // for the partners that have not expressed one.
                        return new CqApiResult<string>(xchangeId)
                        {
                            Status = par.Partner.AnswersOkWhenEmpty(
                                BitweenSettings.ApiCallSubscriptionResponseAcceptedStatusCode)
                                ? CqApiResultStatus.Ok
                                : CqApiResultStatus.UnderProcessing
                        };
                    }
                    case true when xchangeResult.ResponseSize != 0:
                    {
                        var response = await xchangeService.GetFile(xchangeId, XchangeFileType.Response);
                        var result = new CqApiResult<string>(response);
                        result.AddHeader("location", xchangeId);
                        result.Status = xchangeResult.ResponseBad ? CqApiResultStatus.Error : CqApiResultStatus.Ok;
                        result.ContentType = xchangeResult.ResponseContentType ?? MediaTypeNames.Application.Json;
                        return result;
                    }
                    case false:
                        throw new SWValidationException("failure", "Internal processing error.");
                }
            }

            return new CqApiResult<string>(xchangeId)
            {
                Status = CqApiResultStatus.UnderProcessing
            };
        }

        private async Task<bool> IsResultAvailable(string xchangeId)
        {
            return await dbContext.Set<XchangeResult>()
                .AsNoTracking()
                .AnyAsync(i => i.Id == xchangeId);
        }
    }
}