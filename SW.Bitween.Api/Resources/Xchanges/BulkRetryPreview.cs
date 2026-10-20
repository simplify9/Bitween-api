using System.Threading.Tasks;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Xchanges;

/// <summary>
/// What <see cref="BulkRetry"/> would do with the same request, without doing any of it.
/// </summary>
/// <remarks>
/// A selection is rarely just itself: exchanges already retried hand over to their newest attempt,
/// ones that have since succeeded or are still running drop out, and two selections in one chain
/// come to the same attempt. Retrying is not undoable, so the caller gets to show all of that and
/// be told the number before anyone commits to it.
/// <para>
/// It works out a selection past what one retry carries out, up to
/// <see cref="BulkRetryPlanner.MaxSelection"/>, so the caller can retry it in rounds from the plan's
/// targets.
/// </para>
/// </remarks>
[HandlerName("bulkretrypreview")]
public class BulkRetryPreview : ICommandHandler<XchangeBulkRetry, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public BulkRetryPreview(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(XchangeBulkRetry request)
    {
        // Reads nothing a retryer could not already see, but it is the retry dialog's own call and
        // describes an action only they can take, so it is gated with the action.
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Exchanges.Operate);

        var prepared = await new BulkRetryPlanner(_dbContext).Prepare(request, BulkRetryPlanner.MaxSelection);
        return prepared.Plan;
    }
}
