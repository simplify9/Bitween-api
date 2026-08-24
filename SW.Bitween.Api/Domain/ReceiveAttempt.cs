using System;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;

/// <summary>
/// One execution of a Receiving subscription's receive step, written by <c>ReceivingJob</c>
/// itself right where it already catches the receiver's own failures — kept independent of
/// Quartz's own run history and unaffected by how Quartz treats a thrown exception.
/// </summary>
public class ReceiveAttempt : BaseEntity
{
    public int SubscriptionId { get; set; }
    public DateTime StartedOn { get; set; }
    public DateTime FinishedOn { get; set; }
    public ReceiveOutcome Outcome { get; set; }
    public string ErrorMessage { get; set; }
    public string[] ExchangeIds { get; set; } = Array.Empty<string>();
}
