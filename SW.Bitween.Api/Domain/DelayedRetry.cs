using System;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;
// Id should be the same for xchangeId when retry happens the record is deleted
public class DelayedRetry : BaseEntity<string>
{
    public DateTime On { get; set; }

    /// <summary>
    /// How many times running this retry has failed. A failure is rescheduled, not dropped — a
    /// database blip used to delete the retry for good — up to <see cref="RetryJob.MaxRunFailures"/>.
    /// </summary>
    public int RunFailures { get; set; }
}
