using System;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;

/// <summary>
/// Raised the one time a retry group's <c>MaxAttemptsTotal</c> runs out for a subscription, so the
/// configured alert handler can be told that failures matching that group have stopped being retried.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not an <c>IHasWorkGroup</c> event: it publishes under its own type name and is picked
/// up by a dedicated <c>IConsume&lt;RetryBudgetExhaustedEvent&gt;</c> consumer with its own queue. A
/// slow or broken alert handler therefore cannot delay or fail the ordinary notifier path, which
/// shares the work group's result queue.
/// </para>
/// <para>
/// Carried on <see cref="XchangeResult"/> rather than published directly, so it only reaches the bus
/// once the failure it describes has actually been committed.
/// </para>
/// </remarks>
public class RetryBudgetExhaustedEvent : BaseDomainEvent
{
    /// <summary>The failure that found the budget empty.</summary>
    public string XchangeId { get; set; }

    public int SubscriptionId { get; set; }

    public Guid GroupId { get; set; }

    /// <summary>The group's name as it was when the budget ran out, in case it is later renamed.</summary>
    public string GroupName { get; set; }

    /// <summary>
    /// The ceiling that was reached. Not paired with an "used" count, because at exhaustion the two
    /// are the same number — except when the ceiling was lowered below what had already been spent,
    /// where the ceiling is still the meaningful figure.
    /// </summary>
    public int MaxAttemptsTotal { get; set; }

    public DateTime OccurredOn { get; set; }
}
