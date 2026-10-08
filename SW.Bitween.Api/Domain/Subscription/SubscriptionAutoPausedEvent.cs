using System;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;

/// <summary>
/// A subscription paused itself after failing <see cref="Failures"/> deliveries in a row. Picked up
/// by its own consumer, as <see cref="RetryBudgetExhaustedEvent"/> is, so a broken alert handler
/// holds up nothing else.
/// </summary>
public class SubscriptionAutoPausedEvent : BaseDomainEvent
{
    public int SubscriptionId { get; set; }

    /// <summary>The failure that paused it.</summary>
    public string XchangeId { get; set; }

    public int Failures { get; set; }
    public DateTime OccurredOn { get; set; }
}
