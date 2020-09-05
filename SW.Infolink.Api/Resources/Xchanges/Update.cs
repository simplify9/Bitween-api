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
            var ss = serviceProvider.GetServices(typeof(IHandle<XchangeCreatedEvent>));
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
                await xchangeService.RunFilterXchange(document.Id, new XchangeFile(request.ToString()));
            }
            else
            {
                //validate the partner key and get sub id
                //create an sub based xchange

                //await Task.Delay()  
            }

            return null;

            //if (request.SubscriberId > 0)
            //{
            //    var id = await xchangeService.Submit(request.SubscriberId,
            //        request.File,
            //        request.References,
            //        false);

            //    return id.ToString();

            //}
            //else if (request.DocumentId > 0)
            //{
            //    var subscribers = filterService.Filter(request.DocumentId, request.File);

            //    foreach (var sub in subscribers)
            //    {
            //        await xchangeService.Submit(sub, request.File);
            //    }

            //    return null;
            //}
            //else
            //{
            //    throw new ArgumentException();
            //}
        }
    }
}
