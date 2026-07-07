using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
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
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

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
