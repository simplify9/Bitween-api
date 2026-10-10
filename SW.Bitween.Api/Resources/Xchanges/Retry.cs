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
        private readonly RequestContext requestContext;
        private readonly XchangeService xchangeService;

        public Retry(BitweenDbContext dbContext, RequestContext requestContext,
            XchangeService xchangeService)
        {
            this.dbContext = dbContext;
            this.requestContext = requestContext;
            this.xchangeService = xchangeService;
        }

        public async Task<object> Handle(string key, XchangeRetry xchangeRetry)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Exchanges.Operate);

            if (await dbContext.Set<DelayedRetry>().AnyAsync(d => d.Id == key))
                throw new SWValidationException("AUTO_RETRY_SCHEDULED",
                    "An auto-retry is already scheduled for this exchange. Use \"Run Now\" to execute it immediately instead of retrying manually.");

            var xchange = await dbContext.FindAsync<Xchange>(key)
                         ?? throw new SWNotFoundException(key.ToString());
            var inputFileData = await xchangeService.GetFile(xchange, XchangeFileType.Input);
            var xchangeFile = new XchangeFile(inputFileData, xchange.InputName);
            var subscription = await dbContext.Subscriptions().FirstOrDefaultAsync(s => s.Id == xchange.SubscriptionId);
            if (xchangeRetry.Reset)
            {
                
                if (subscription == null)
                    throw new SWValidationException("SUBSCRIPTION_NOT_FOUND",
                        "Cant reset properties, subscription doesnt exist anymore");
                await xchangeService.CreateXchange(subscription, xchange, xchangeFile, manualRetry: true);
            }
            else
            {
                await xchangeService.CreateXchange(xchange, xchangeFile, subscription?.WorkGroup,
                    manualRetry: true);
            }
            dbContext.Record(requestContext, OperatorAction.Retry, key,
                xchangeRetry.Reset ? "with the subscription's current settings" : null);
            await dbContext.SaveChangesAsync();

            return null;
        }
    }
}
