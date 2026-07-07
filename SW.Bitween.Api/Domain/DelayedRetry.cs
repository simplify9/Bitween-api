using System;
using System.Collections.Generic;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;
// Id should be the same for xchangeId when retry happens the record is deleted
public class DelayedRetry : BaseEntity<string>
{
    public DateTime On { get; set; }
    public Dictionary<string, int> GroupAttemptCounts { get; set; } = new();
}
