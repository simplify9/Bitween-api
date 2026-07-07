using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.PrimitiveTypes;
using SW.Bitween.Model;
using SW.Bitween.Domain;

namespace SW.Bitween.Resources.Xchanges
{
    [HandlerName("retry")]
    public class Retry : ICommandHandler<string, XchangeRetry,object>
    {
        private readonly BitweenDbContext dbContext;
        private readonly XchangeService xchangeService;

        public Retry(BitweenDbContext dbContext, XchangeService xchangeService)
        {
            this.dbContext = dbContext;
            this.xchangeService = xchangeService;
        }

        public async Task<object> Handle(string key, XchangeRetry xchangeRetry)
        {
            if (await dbContext.Set<DelayedRetry>().AnyAsync(d => d.Id == key))
                throw new SWValidationException("AUTO_RETRY_SCHEDULED",
                    "An auto-retry is already scheduled for this exchange. Use \"Run Now\" to execute it immediately instead of retrying manually.");

            var xchange = await dbContext.FindAsync<Xchange>(key);
            var inputFileData = await xchangeService.GetFile(xchange.Id, XchangeFileType.Input);
            var xchangeFile = new XchangeFile(inputFileData, xchange.InputName);
            var subscription = await dbContext.Subscriptions().FirstOrDefaultAsync(s => s.Id == xchange.SubscriptionId);
            if (xchangeRetry.Reset)
            {
                
                if (subscription == null)
                    throw new SWValidationException("SUBSCRIPTION_NOT_FOUND",
                        "Cant reset properties, subscription doesnt exist anymore");
                await xchangeService.CreateXchange(subscription, xchange, xchangeFile);
            }
            else
            {
                await xchangeService.CreateXchange(xchange,xchangeFile,subscription?.WorkGroup );
            }
            
            
            await dbContext.SaveChangesAsync();
            
            return null;
        }
    }
}
