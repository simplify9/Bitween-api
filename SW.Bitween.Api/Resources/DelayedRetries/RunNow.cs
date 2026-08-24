using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.DelayedRetries
{
    [HandlerName("runnow")]
    public class RunNow : ICommandHandler<string, DelayedRetryRunNow, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;
        private readonly XchangeService _xchangeService;

        public RunNow(BitweenDbContext dbContext, RequestContext requestContext, XchangeService xchangeService)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
            _xchangeService = xchangeService;
        }

        public async Task<object> Handle(string key, DelayedRetryRunNow request)
        {
            // What's being operated on is an exchange, not the policy that scheduled the retry —
            // and this is the same page the UI gates on exchange permissions.
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Exchanges.Operate);

            var delayedRetry = await _dbContext.Set<DelayedRetry>().FirstOrDefaultAsync(d => d.Id == key);
            if (delayedRetry == null)
                throw new SWValidationException("NOT_FOUND", "No auto-retry is currently scheduled for this exchange.");

            if (!await _xchangeService.ExecuteDelayedRetry(delayedRetry))
            {
                await _dbContext.SaveChangesAsync();

                // Every other refusal writes its reason onto the exchange, so the message sends the
                // caller there instead of listing them. A missing exchange is the one case with
                // nowhere to write it, and pointing at something that is gone explains nothing.
                var exchangeExists = await _dbContext.Set<Xchange>().AnyAsync(x => x.Id == key);

                throw new SWValidationException("CANNOT_RETRY", exchangeExists
                    ? "This retry could not be carried out. The exchange it belongs to says why."
                    : "This retry could not be carried out: the exchange it belonged to no longer exists.");
            }

            await _dbContext.SaveChangesAsync();
            return null;
        }
    }
}
