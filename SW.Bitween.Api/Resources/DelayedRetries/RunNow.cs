using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.DelayedRetries
{
    [HandlerName("runnow")]
    public class RunNow(BitweenDbContext dbContext, RequestContext requestContext,
        XchangeService xchangeService) : ICommandHandler<string, DelayedRetryRunNow, object>
    {
        public async Task<object> Handle(string key, DelayedRetryRunNow request)
        {
            // What's being operated on is an exchange, not the policy that scheduled the retry —
            // and this is the same page the UI gates on exchange permissions.
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Exchanges.Operate);

            var delayedRetry = await dbContext.Set<DelayedRetry>().FirstOrDefaultAsync(d => d.Id == key);
            if (delayedRetry == null)
                throw new SWValidationException("NOT_FOUND", "No auto-retry is currently scheduled for this exchange.");

            if (!await xchangeService.ExecuteDelayedRetry(delayedRetry))
            {
                await dbContext.SaveChangesAsync();

                // Every other refusal writes its reason onto the exchange, so the message sends the
                // caller there instead of listing them. A missing exchange is the one case with
                // nowhere to write it, and pointing at something that is gone explains nothing.
                var exchangeExists = await dbContext.Set<Xchange>().AnyAsync(x => x.Id == key);

                throw new SWValidationException("CANNOT_RETRY", exchangeExists
                    ? "This retry could not be carried out. The exchange it belongs to says why."
                    : "This retry could not be carried out: the exchange it belonged to no longer exists.");
            }

            dbContext.Record(requestContext, OperatorAction.RunRetryNow, key);
            await dbContext.SaveChangesAsync();
            return null;
        }
    }
}
