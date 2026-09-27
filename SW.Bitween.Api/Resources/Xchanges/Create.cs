using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Xchanges
{
    public class Create(XchangeService xchangeService, BitweenDbContext dbc) : ICommandHandler<CreateXchange,object>
    {
        public async Task<object> Handle(CreateXchange request)
        {
            var xchangeFile = new XchangeFile(request.Data, "manual.json");
            if (request.Option == CreateXchangeOption.DocumentId)
            {
                var document = await dbc.Set<Document>().FirstOrDefaultAsync(d => d.Id == request.DocumentId);
                if (document == null) throw new SWValidationException("DOCUMENT_NOT_FOUND", "Document was not found");
                await xchangeService.CreateXchange(document,WorkGroup.None,  xchangeFile);
            }
            else if (request.Option == CreateXchangeOption.SubscriberId)
            {
                var subscription = await dbc.Subscriptions().FirstOrDefaultAsync(d => d.Id == request.SubscriberId);
                if (subscription == null) throw new SWValidationException("SUBSCRIPTION_NOT_FOUND", "Subscription was not found");
                await xchangeService.CreateXchange(subscription, xchangeFile);
            }
            else
            {
                throw new NotImplementedException();
            }

            await dbc.SaveChangesAsync();

            return null;

        }
    }
}