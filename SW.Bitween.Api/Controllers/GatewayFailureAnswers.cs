using System;
using SW.Bitween.Model;

namespace SW.Bitween.Controllers;

/// <summary>
/// What a sync gateway call is told when the delivery behind it failed. Legacy says 400 whatever
/// happened, which tells the caller its request was wrong: in the lab, orders refused by a warehouse
/// that was down were answered 400 and then delivered by retry minutes later, so the marketplace was
/// told "rejected" about orders that went through. Accurate says what Bitween will do next.
/// </summary>
public static class GatewayFailureAnswers
{
    public record Answer(int Status, object Body);

    /// <param name="hasMapper">Whether the subscription maps the message before delivering it.</param>
    /// <param name="mapped">Whether mapping produced an output, so the failure came after it.</param>
    /// <param name="retryOn">When the retry Bitween scheduled is due; null when there is none.</param>
    public static Answer For(GatewayFailureAnswer mode, string exchangeId, bool hasMapper, bool mapped, DateTime? retryOn)
    {
        if (mode == GatewayFailureAnswer.Legacy) return new Answer(400, null);

        // Bitween has the message and will deliver it again: sending it again would make a second one.
        if (retryOn is { } next)
            return new Answer(202, new { exchangeId, status = "retrying", nextAttemptOn = next });

        if (hasMapper && !mapped)
            return new Answer(422, new
            {
                exchangeId, status = "failed",
                error = "The message couldn't be mapped to what the destination takes. It won't be retried.",
            });

        return new Answer(502, new
        {
            exchangeId, status = "failed",
            error = "The delivery to the destination failed. It won't be retried.",
        });
    }
}
