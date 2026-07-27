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
                throw new SWValidationException("NOT_FOUND", "The original exchange or its subscription no longer exists.");

            await _dbContext.SaveChangesAsync();
            return null;
        }
    }
}
