using System.Threading.Tasks;
using SW.PrimitiveTypes;
using SW.Bitween.Model;
using SW.Bitween.Domain;

namespace SW.Bitween.Resources.Xchanges
{
    [HandlerName("retry")]
    class Retry : ICommandHandler<string, XchangeRetry,object>
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
            var xchange = await dbContext.FindAsync<Xchange>(key);
            var inputFileData = await xchangeService.GetFile(xchange.Id, XchangeFileType.Input);
            var xchangeFile = new XchangeFile(inputFileData, xchange.InputName);
            

            if (xchangeRetry.Reset)
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
            
            
            await dbContext.SaveChangesAsync();
            
            return null;
        }
    }
}
