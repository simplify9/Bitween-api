using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Resources.Xchanges
{
    [Unprotect]
    class Update : ICommandHandler<string, object>
    {
        private readonly RequestContext _requestContext;
        private readonly XchangeService _xchangeService;
        private readonly InfolinkDbContext _dbContext;
        private readonly InfolinkOptions _infolinkSettings;
        private readonly IInfolinkCache _cache;

        public Update(RequestContext requestContext, XchangeService xchangeService, InfolinkDbContext dbContext,
            InfolinkOptions infolinkSettings, IInfolinkCache cache)
        {
            _requestContext = requestContext;
            _xchangeService = xchangeService;
            _dbContext = dbContext;
            _infolinkSettings = infolinkSettings;
            _cache = cache;
        }

        public async Task<object> Handle(string documentIdOrName, object request)
        {
            Document document;

            if (int.TryParse(documentIdOrName, out var documentId))
                document = await _cache.DocumentByIdAsync(documentId);
            else
                document = await _cache.DocumentByNameAsync(documentIdOrName);

            if (document is null)
                throw new SWNotFoundException("Document");

            var par = await _dbContext.AuthorizePartner(_requestContext);


            var subs = (await _cache.ListSubscriptionsByDocumentAsync(document.Id))
                .Where(i => i.PartnerId == par.Partner.Id)
                .ToList();

            if (subs?.Count > 1)
                throw new SWValidationException("Subscriptions",
                    "You can only have one subscription of for each document");

            var sub = subs.SingleOrDefault();

            if (par.Partner.Id == Partner.SystemId && sub is null)
            {
                await _xchangeService.SubmitFilterXchange(document.Id, new XchangeFile(request.ToString()));
                return null;
            }

            if (sub is null)
                throw new SWNotFoundException("No subscription of type ApiCall was found for this document");


            var xchangeReferences = new List<string> { $"partnerkey: {par.KeyName}" };

            var waitResponseHeader = _requestContext.Values
                .Where(item => item.Name.ToLower() == "waitresponse")
                .Select(item => item.Value).FirstOrDefault();

            var waitResponse = 0;
            if (int.TryParse(waitResponseHeader, out var waitResponseValue))
            {
                waitResponse = waitResponseValue <= 0 ? 120 : waitResponseValue;
                xchangeReferences.Add($"waitresponse: {waitResponse}");
            }

            var xchangeFile = new XchangeFile(request.ToString());

            await _xchangeService.RunValidator(sub.ValidatorId, sub.ValidatorProperties.ToDictionary(), xchangeFile);

            var xchangeId =
                await _xchangeService.SubmitSubscriptionXchange(sub.Id, xchangeFile, xchangeReferences.ToArray());

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

                var xchangeResult = await _dbContext.FindAsync<XchangeResult>(xchangeId);


                switch (xchangeResult!.Success)
                {
                    case true when xchangeResult.ResponseSize == 0:
                    {
                        return new CqApiResult<string>(xchangeId)
                        {
                            Status = _infolinkSettings.ApiCallSubscriptionResponseAcceptedStatusCode == 200
                                ? CqApiResultStatus.Ok
                                : CqApiResultStatus.UnderProcessing
                        };
                    }
                    case true when xchangeResult.ResponseSize != 0:
                    {
                        var response = await _xchangeService.GetFile(xchangeId, XchangeFileType.Response);
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
            return await _dbContext.Set<XchangeResult>()
                .AsNoTracking()
                .AnyAsync(i => i.Id == xchangeId);
        }
    }
}