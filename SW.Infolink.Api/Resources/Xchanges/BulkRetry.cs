using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Xchanges
{
    [HandlerName("bulkretry")]
    public class BulkRetry: ICommandHandler<XchangeBulkRetry>
    {
        private readonly InfolinkDbContext dbContext;
        private readonly XchangeService xchangeService;

        public BulkRetry(InfolinkDbContext dbContext, XchangeService xchangeService)
        {
            this.dbContext = dbContext;
            this.xchangeService = xchangeService;
        }
        
        public async Task<object> Handle(XchangeBulkRetry request)
        {
            var xchanges = await dbContext.Set<Xchange>().Where(c => request.Ids.Contains(c.Id)).AsNoTracking().ToListAsync();

            foreach (var xchange in xchanges)
            {
                var inputFileData = await xchangeService.GetFile(xchange.Id, XchangeFileType.Input);
                var xchangeFile = new XchangeFile(inputFileData, xchange.InputName);
                if (request.Reset)
                {
                    var subscription = await dbContext.FindAsync<Subscription>(xchange.SubscriptionId);
                    if (subscription == null)
                        throw new SWValidationException("SUBSCRIPTION_NOT_FOUND",
                            "Cant reset properties, subscription doesnt exist anymore");
                    await xchangeService.CreateXchange(subscription, xchange, xchangeFile);
                }
                else
                {
                    await xchangeService.CreateXchange(xchange, xchangeFile);
                }
            }
            
            await dbContext.SaveChangesAsync();
            
            return null;
        }
    }
}