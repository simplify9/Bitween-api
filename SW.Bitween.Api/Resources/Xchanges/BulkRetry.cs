using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Xchanges
{
    /// <summary>
    /// Retries a selection of exchanges — either a hand-picked list of ids or everything a filter
    /// matches. Returns what it did: <see cref="BulkRetryPreview"/> answers the same question
    /// beforehand, so the caller can show it and be sure the two agree.
    /// </summary>
    [HandlerName("bulkretry")]
    public class BulkRetry : ICommandHandler<XchangeBulkRetry, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;
        private readonly XchangeService _xchangeService;


        public BulkRetry(BitweenDbContext dbContext, RequestContext requestContext,
            XchangeService xchangeService)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
            _xchangeService = xchangeService;
        }

        public async Task<object> Handle(XchangeBulkRetry request)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Exchanges.Operate);

            var prepared = await new BulkRetryPlanner(_dbContext).Prepare(request);
            var plan = prepared.Plan;

            if (plan.OverLimit)
                throw new SWValidationException("TOO_MANY",
                    $"{plan.Selected:n0} exchanges is more than the {BulkRetryPlanner.Limit} this can retry " +
                    "in one go. Narrow the filter and retry the rest after.");

            var retried = 0;
            foreach (var id in prepared.Targets)
            {
                var xchange = await _dbContext.Set<Xchange>().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == id);
                if (xchange == null)
                {
                    plan.Skipped.Add(new XchangeRetrySkip
                    {
                        Id = id,
                        Reason = "This exchange no longer exists."
                    });
                    continue;
                }

                var subscription = await _dbContext.Subscriptions()
                    .FirstOrDefaultAsync(s => s.Id == xchange.SubscriptionId);

                if (request.Reset && subscription == null)
                {
                    // Reported per exchange rather than thrown. Throwing meant one exchange whose
                    // subscription had since been deleted took the whole selection down with it,
                    // and the caller could not tell which one.
                    plan.Skipped.Add(new XchangeRetrySkip
                    {
                        Id = id,
                        Reason = "Its properties cannot be reset: the subscription no longer exists."
                    });
                    continue;
                }

                // The tolerant read, not GetFile: a retry re-sends the original input, so an
                // exchange whose input has been deleted or expired cannot be retried — and one of
                // those in a selection of five hundred must not take the other 499 with it.
                var xchangeFile = await _xchangeService.ReadInputFile(xchange);
                if (xchangeFile == null)
                {
                    plan.Skipped.Add(new XchangeRetrySkip
                    {
                        Id = id,
                        Reason = "Its input document could not be read, so there is nothing to re-send."
                    });
                    continue;
                }

                try
                {
                    if (request.Reset)
                    {
                        await _xchangeService.CreateXchange(subscription, xchange, xchangeFile,
                            manualRetry: true);
                    }
                    else
                    {
                        // Null when the subscription has since been deleted, which a document-only
                        // exchange also has from the start. The single-exchange retry has always allowed
                        // for it; without the same here, one such id in a selection threw and took the
                        // whole bulk retry down with it.
                        await _xchangeService.CreateXchange(xchange, xchangeFile, subscription?.WorkGroup,
                            manualRetry: true);
                    }
                }
                catch (SWValidationException e) when (
                    e.Validations.Any(v => v.Key == "ALREADY_RETRIED"))
                {
                    // The planner resolves every selection to the end of its chain, so this is not
                    // reachable by choosing badly — it means someone retried this attempt in the
                    // moment between the plan being worked out and it being carried out. Reported
                    // like the other per-exchange refusals rather than thrown, so one racing
                    // operator cannot cancel another's whole recovery.
                    plan.Skipped.Add(new XchangeRetrySkip
                    {
                        Id = id,
                        Reason = "It was retried by someone else a moment ago. Its own retry can be retried instead."
                    });
                    continue;
                }

                retried++;
            }

            if (retried > 0)
                _dbContext.Record(_requestContext, OperatorAction.BulkRetry, $"{retried} exchanges",
                    $"{retried} retried, {plan.Skipped?.Count ?? 0} skipped");
            await _dbContext.SaveChangesAsync();

            plan.WillRetry = retried;
            return plan;
        }
    }
}
