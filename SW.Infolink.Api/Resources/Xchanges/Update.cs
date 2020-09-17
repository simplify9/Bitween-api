using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
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

        public Update(RequestContext requestContext, XchangeService xchangeService, InfolinkDbContext dbContext, IServiceProvider serviceProvider)
        {
            this.requestContext = requestContext;
            this.xchangeService = xchangeService;
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(string documentIdOrName, object request)
        {
            Document document = null;

            if (int.TryParse(documentIdOrName, out var documentId))
                document = await dbContext.FindAsync<Document>(documentId);
            else
                document = await dbContext.Set<Document>().Where(doc => doc.Name == documentIdOrName).SingleOrDefaultAsync();

            if (document == null)
                throw new SWNotFoundException("Document");

            var par = await dbContext.AuthorizePartner(requestContext);

            if (par.Id == Partner.SystemId)
            {
                await xchangeService.SubmitFilterXchange(document.Id, new XchangeFile(request.ToString()));
                return null;
            }

            var subscriptionQuery = from subscription in dbContext.Set<Subscription>()
                                    where subscription.DocumentId == document.Id && subscription.PartnerId == par.Id
                                    select subscription;

            var sub = await subscriptionQuery.AsNoTracking().SingleOrDefaultAsync();

            if (sub == null)
                throw new SWNotFoundException("Subscription");

            var xchangeFile = new XchangeFile(request.ToString());

            await xchangeService.RunValidator(sub.ValidatorId, sub.ValidatorProperties.ToDictionary(), xchangeFile);

            var xchangeId = await xchangeService.SubmitSubscriptionXchange(sub.Id, xchangeFile);

            var waitResponseHeader = requestContext.Values.Where(item => item.Name.ToLower() == "waitresponse").Select(item => item.Value).FirstOrDefault();
            if (int.TryParse(waitResponseHeader, out var waitResponse) && waitResponse > 0 && waitResponse <= 60)
            {
                await Task.Delay(TimeSpan.FromSeconds(waitResponse));
                var xchangeResult = await dbContext.FindAsync<XchangeResult>(xchangeId);
                if (xchangeResult != null)
                {
                    if (xchangeResult.Success && xchangeResult.ResponseSize != 0)
                    {
                        var response = await xchangeService.GetFile(xchangeId, XchangeFileType.Response);
                        //var jsonContent = new StringContent(response, Encoding.UTF8, MediaTypeNames.Application.Json);

                        if (xchangeResult.ResponseBad)
                        {
                            throw new SWException(response);
                        }
                        else
                        {
                            var responseWithHeaders = new ResultWithHeaders<string>(response);
                            responseWithHeaders.AddHeader("location", xchangeId);
                            responseWithHeaders.AddHeader("content-type", MediaTypeNames.Application.Json);

                            return responseWithHeaders;
                        }

                    }
                    else if (!xchangeResult.Success)
                    {
                        throw new SWValidationException("failure", "Internal processing error.");
                    }
                }
            }

            return new XchangeUnderProcessing
            {
                Uri = xchangeId
            };
        }
    }
}
