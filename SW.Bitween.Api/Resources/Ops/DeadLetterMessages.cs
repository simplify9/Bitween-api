using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Ops;

public class DeadLetterMessagesQuery
{
    /// <summary>A dead-letter queue, as <c>/ops/deadletters</c> names it.</summary>
    public string Queue { get; set; }
    public int Count { get; set; } = 20;
}

/// <summary>
/// What is sitting in one dead-letter queue. The bodies are the messages themselves — a partner's
/// payload, often — so seeing them takes the right to see exchanges too.
/// </summary>
[HandlerName("DeadLetterMessages")]
public class DeadLetterMessages(DeadLetterQueues deadLetters, BitweenDbContext dbContext, RequestContext requestContext)
    : IQueryHandler<DeadLetterMessagesQuery, object>
{
    public async Task<object> Handle(DeadLetterMessagesQuery request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Monitoring.View);
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Exchanges.View);

        var queue = await deadLetters.Resolve(request.Queue)
                    ?? throw new SWNotFoundException("Dead-letter queue");
        return await deadLetters.Browse(queue, request.Count);
    }
}
