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
        private readonly RequestContext requestContext;
        private readonly XchangeService xchangeService;
        private readonly InfolinkDbContext dbContext;
        private readonly InfolinkOptions infolinkSettings;

        public Update(RequestContext requestContext, XchangeService xchangeService, InfolinkDbContext dbContext,
            InfolinkOptions infolinkSettings)
        {
            this.requestContext = requestContext;
            this.xchangeService = xchangeService;
            this.dbContext = dbContext;
            this.infolinkSettings = infolinkSettings;
        }

        public async Task<object> Handle(string documentIdOrName, object request)
        {
            Document document;

            if (int.TryParse(documentIdOrName, out var documentId))
                document = await dbContext.FindAsync<Document>(documentId);
            else
                document = await dbContext.Set<Document>()
                    .Where(doc => doc.Name.ToLower() == documentIdOrName.ToLower())
                    .SingleOrDefaultAsync();

            if (document == null)
                throw new SWNotFoundException("Document");

            var par = await dbContext.AuthorizePartner(requestContext);
            var subscriptionQuery = from subscription in dbContext.Set<Subscription>()
                where subscription.DocumentId == document.Id && subscription.PartnerId == par.Partner.Id
                select subscription;

            var sub = await subscriptionQuery.AsNoTracking().SingleOrDefaultAsync();

            if (par.Partner.Id == Partner.SystemId && sub == null)
            {
                await xchangeService.SubmitFilterXchange(document.Id, new XchangeFile(request.ToString()));
                return null;
            }

            if (sub == null)
                throw new SWNotFoundException("Subscription");

            if (sub.Type != SubscriptionType.ApiCall)
                throw new SWValidationException("Subscription", $"Subscription is of wrong type {sub.Type}");

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

            await xchangeService.RunValidator(sub.ValidatorId, sub.ValidatorProperties.ToDictionary(), xchangeFile);

            var xchangeId =
                await xchangeService.SubmitSubscriptionXchange(sub.Id, xchangeFile, xchangeReferences.ToArray());

            if (waitResponse <= 0)
                return new CqApiResult<string>(xchangeId)
                {
                    Status = CqApiResultStatus.Ok
                };

            for (double count = 2; count <= waitResponse; count += 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(count));
                if (!await IsResultAvailable(xchangeId)) continue;

                var xchangeResult = await dbContext.FindAsync<XchangeResult>(xchangeId);


                switch (xchangeResult!.Success)
                {
                    case true when xchangeResult.ResponseSize == 0:
                    {
                        return new CqApiResult<string>(xchangeId)
                        {
                            Status = infolinkSettings.ApiCallSubscriptionResponseAcceptedStatusCode == 200
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

        async Task<bool> IsResultAvailable(string xchangeId)
        {
            return await dbContext.Set<XchangeResult>()
                .AsNoTracking()
                .AnyAsync(i => i.Id == xchangeId);
        }
    }
}