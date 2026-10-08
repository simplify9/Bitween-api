using System.Threading.Tasks;
using RabbitMQ.Client.Exceptions;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Ops;

public class RequeueDeadLettersModel
{
    /// <summary>A dead-letter queue, as <c>/ops/deadletters</c> names it.</summary>
    public string Queue { get; set; }

    /// <summary>How many, oldest first. Defaults to all of them, up to <see cref="DeadLetterQueues.MaxRequeue"/>.</summary>
    public int? Count { get; set; }
}

public record RequeueDeadLettersResult(int Requeued);

/// <summary>Sends dead-lettered messages back to the consumer that gave up on them, to be tried again.</summary>
[HandlerName("RequeueDeadLetters")]
public class RequeueDeadLetters(DeadLetterQueues deadLetters, BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<RequeueDeadLettersModel, object>
{
    public async Task<object> Handle(RequeueDeadLettersModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Monitoring.Operate);

        var queue = await deadLetters.Resolve(request.Queue)
                    ?? throw new SWNotFoundException("Dead-letter queue");
        try
        {
            return new RequeueDeadLettersResult(
                deadLetters.Requeue(queue, request.Count ?? DeadLetterQueues.MaxRequeue));
        }
        catch (OperationInterruptedException)
        {
            throw new SWValidationException("NO_MAIN_QUEUE",
                "The queue these messages failed on is gone, so there is nowhere to send them back to.");
        }
    }
}
