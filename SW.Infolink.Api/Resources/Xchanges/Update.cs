using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Infolink.Resources.Xchanges
{
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
            {
                document = await dbContext.FindAsync<Document>(documentId);
            }
            else
            {
                document = await dbContext.Set<Document>().Where(doc => doc.Name == documentIdOrName).SingleOrDefaultAsync();
            }

            if (document == null) throw new InfolinkException("Document name not found.");

            var partnerKey = requestContext.Values.Where(item => item.Name.ToLower() == "partnerkey").Select(item => item.Value).FirstOrDefault();
            if (partnerKey == null)
            {
                await xchangeService.SubmitFilterXchange(document.Id, new XchangeFile(request.ToString()));
            }
            else
            {
                var subscriptionQuery = from subscription in dbContext.Set<Subscription>()
                                        join partner in dbContext.Set<Partner>() on subscription.PartnerId equals partner.Id
                                        where subscription.DocumentId == document.Id && partner.ApiCredentials.Any(cred => cred.Key == partnerKey)
                                        select subscription;

                var sub = await subscriptionQuery.AsNoTracking().SingleOrDefaultAsync();

                var xchangeId = await xchangeService.SubmitSubscriptionXchange(sub.Id, new XchangeFile(request.ToString()));

                var waitResponseHeader = requestContext.Values.Where(item => item.Name.ToLower() == "waitresponse").Select(item => item.Value).FirstOrDefault();
                if (int.TryParse(waitResponseHeader, out var waitResponse))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    var xchangeResult = await dbContext.FindAsync<XchangeResult>(xchangeId);
                    if (xchangeResult != null && xchangeResult.ResponseSize != 0)
                        return await xchangeService.GetFile(xchangeId, XchangeFileType.Response);
                }
            }

            return null;
        }
    }
}
