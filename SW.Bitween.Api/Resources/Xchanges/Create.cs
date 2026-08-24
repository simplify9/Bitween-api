using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Xchanges
{
    public class Create: ICommandHandler<CreateXchange,object>
    {
        private readonly XchangeService _xchangeService;
        private readonly BitweenDbContext _dbc;
        
        public Create(XchangeService xchangeService, BitweenDbContext dbc)
        {
            _xchangeService = xchangeService;
            _dbc = dbc;
        }

        public async Task<object> Handle(CreateXchange request)
        {

            var xchangeFile = new XchangeFile(request.Data, "manual.json");
            if (request.Option == CreateXchangeOption.DocumentId)
            {
                var document = await _dbc.Set<Document>().FirstOrDefaultAsync(d => d.Id == request.DocumentId);
                if (document == null) throw new SWValidationException("DOCUMENT_NOT_FOUND", "Document was not found");
                await _xchangeService.CreateXchange(document,WorkGroup.None,  xchangeFile);
            }
            else if (request.Option == CreateXchangeOption.SubscriberId)
            {
                var subscription = await _dbc.Set<Subscription>().FirstOrDefaultAsync(d => d.Id == request.SubscriberId);
                if (subscription == null) throw new SWValidationException("SUBSCRIPTION_NOT_FOUND", "Subscription was not found");
                await _xchangeService.CreateXchange(subscription, xchangeFile);
            }
            else
            {
                throw new NotImplementedException();
            }

            await _dbc.SaveChangesAsync();

            return null;

        }
    }
}