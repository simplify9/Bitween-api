using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.Services.Background;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Xchanges
{
    /// <summary>
    /// Retries a selection of exchanges - either a hand-picked list of ids or everything a filter
    /// matches. Returns what it did: <see cref="BulkRetryPreview"/> answers the same question
    /// beforehand, so the caller can show it and be sure the two agree.
    /// </summary>
    /// <remarks>
    /// Asked to carry on in the background, as the admin UI asks it, it takes the whole selection a
    /// preview works out - up to <see cref="BulkRetryPlanner.MaxSelection"/> - saving as it goes, saying
    /// how far it has got, and stopping between exchanges when cancelled. It goes at the pipeline's pace:
    /// no more than <see cref="Waiting"/> of its exchanges wait to be processed at once, so a retry of
    /// hundreds of thousands never floods the queues, and the partners behind them, and the day's own
    /// traffic is never more than that many behind it. Asked directly, it keeps to
    /// <see cref="BulkRetryPlanner.Limit"/>, as it always has.
    /// </remarks>
    [HandlerName("bulkretry")]
    public class BulkRetry : ICommandHandler<XchangeBulkRetry, object>
    {
        /// <summary>
        /// How many exchanges go into one save. Each save is its own transaction, so a retry that
        /// stops part way keeps what it did; and no more than this many are held in memory at once.
        /// </summary>
        private const int SaveEvery = 100;

        /// <summary>The most of a background retry's exchanges waiting to be processed at once. Set lower by tests.</summary>
        internal static int Waiting { get; set; } = 1_000;

        /// <summary>How often a retry held back by <see cref="Waiting"/> looks again. Set shorter by tests.</summary>
        internal static System.TimeSpan LookEvery { get; set; } = System.TimeSpan.FromSeconds(1);

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

            var background = BackgroundRequestScope.Current;
            var upTo = background != null ? BulkRetryPlanner.MaxSelection : BulkRetryPlanner.Limit;
            var prepared = await new BulkRetryPlanner(_dbContext).Prepare(request, upTo, listTargets: background == null);
            var plan = prepared.Plan;

            if (plan.OverLimit)
                throw new SWValidationException("TOO_MANY", background != null
                    ? $"{plan.Selected:n0} exchanges is more than the {BulkRetryPlanner.MaxSelection:n0} one retry " +
                      "takes. Narrow the selection."
                    : $"{plan.Selected:n0} exchanges is more than the {BulkRetryPlanner.Limit} this can retry " +
                      $"in one go. Send the preview's targets back in rounds of {BulkRetryPlanner.Limit}.");

            var total = prepared.Targets.Count;
            var retried = 0;
            var handled = 0;
            // The retries this run made that the pipeline hasn't finished, and this save's targets.
            var unfinished = new List<string>();
            var batch = new List<string>();
            background?.Progress(0, total, Said(0, plan));
            foreach (var id in prepared.Targets)
            {
                // Between exchanges only, so the one under way is either done or not started.
                if (background?.CancelRequested.IsCancellationRequested == true ||
                    background != null && !await RoomFor(unfinished, background, handled, total, retried, plan))
                {
                    plan.StoppedAfter = handled;
                    break;
                }

                if (await RetryOne(id, request, plan)) retried++;
                handled++;
                batch.Add(id);

                if (handled % SaveEvery != 0) continue;
                await _dbContext.SaveChangesAsync();
                _dbContext.ChangeTracker.Clear();
                if (background != null) unfinished.AddRange(await RetriesOf(batch));
                batch.Clear();
                background?.Progress(handled, total, Said(retried, plan));
            }

            if (retried > 0)
                _dbContext.Record(_requestContext, OperatorAction.BulkRetry, $"{retried} exchanges",
                    $"{retried} retried, {plan.SkippedCount} skipped" +
                    (plan.StoppedAfter is { } stopped ? $", cancelled after {stopped} of {total}" : ""));
            await _dbContext.SaveChangesAsync();

            plan.WillRetry = retried;
            background?.Progress(handled, total, Said(retried, plan));
            return plan;
        }

        /// <summary>How far it has got, in the words the header shows.</summary>
        private static string Said(int retried, XchangeBulkRetryPlan plan) =>
            $"{retried:n0} retried, {plan.SkippedCount:n0} skipped";

        /// <summary>
        /// Waits while <see cref="Waiting"/> of this run's retries are still to be processed. Each one
        /// the pipeline finishes is progress, which keeps the retry's limit from running out; a pipeline
        /// that finishes none for the whole limit - stopped, or a paused subscription holding them - ends
        /// it there. False when its owner cancels it meanwhile.
        /// </summary>
        private async Task<bool> RoomFor(List<string> unfinished, BackgroundRequestScope background, int handled,
            int total, int retried, XchangeBulkRetryPlan plan)
        {
            while (unfinished.Count >= Waiting)
            {
                var finished = (await _dbContext.Set<XchangeResult>().AsNoTracking()
                    .Where(r => unfinished.Contains(r.Id)).Select(r => r.Id).ToListAsync()).ToHashSet();
                if (finished.Count > 0)
                {
                    unfinished.RemoveAll(finished.Contains);
                    background.Progress(handled, total,
                        $"{Said(retried, plan)} · waiting for the pipeline, {unfinished.Count:n0} to go");
                    continue;
                }

                if (background.CancelRequested.IsCancellationRequested) return false;
                await Task.Delay(LookEvery, background.Stopped);
            }

            return true;
        }

        /// <summary>The retries just saved for <paramref name="targets"/>.</summary>
        private Task<List<string>> RetriesOf(List<string> targets) =>
            _dbContext.Set<Xchange>().AsNoTracking()
                .Where(x => targets.Contains(x.RetryFor)).Select(x => x.Id).ToListAsync();

        /// <summary>Retries one target, or says in the plan why it didn't; true when it did.</summary>
        private async Task<bool> RetryOne(string id, XchangeBulkRetry request, XchangeBulkRetryPlan plan)
        {
            var xchange = await _dbContext.Set<Xchange>().AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);
            if (xchange == null)
            {
                BulkRetryPlanner.Skip(plan, new XchangeRetrySkip
                {
                    Id = id,
                    Reason = "This exchange no longer exists."
                });
                return false;
            }

            var subscription = await _dbContext.Subscriptions()
                .FirstOrDefaultAsync(s => s.Id == xchange.SubscriptionId);

            if (request.Reset && subscription == null)
            {
                // Reported per exchange rather than thrown. Throwing meant one exchange whose
                // subscription had since been deleted took the whole selection down with it,
                // and the caller could not tell which one.
                BulkRetryPlanner.Skip(plan, new XchangeRetrySkip
                {
                    Id = id,
                    Reason = "Its properties cannot be reset: the subscription no longer exists."
                });
                return false;
            }

            // The tolerant read, not GetFile: a retry re-sends the original input, so an
            // exchange whose input has been deleted or expired cannot be retried - and one of
            // those in a selection of five hundred must not take the other 499 with it.
            var xchangeFile = await _xchangeService.ReadInputFile(xchange);
            if (xchangeFile == null)
            {
                BulkRetryPlanner.Skip(plan, new XchangeRetrySkip
                {
                    Id = id,
                    Reason = "Its input document could not be read, so there is nothing to re-send."
                });
                return false;
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
                // reachable by choosing badly - it means someone retried this attempt in the
                // moment between the plan being worked out and it being carried out. Reported
                // like the other per-exchange refusals rather than thrown, so one racing
                // operator cannot cancel another's whole recovery.
                BulkRetryPlanner.Skip(plan, new XchangeRetrySkip
                {
                    Id = id,
                    Reason = "It was retried by someone else a moment ago. Its own retry can be retried instead."
                });
                return false;
            }

            return true;
        }
    }
}
