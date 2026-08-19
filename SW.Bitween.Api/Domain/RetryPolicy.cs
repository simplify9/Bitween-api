using System;
using System.Collections.Generic;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;
// templates of retry policy to choose on subscriptions
public class RetryPolicy : BaseEntity, IAudited, IRetryPolicy
{
    public string Name { get; set; }
    public List<RetryGroup> Groups { get; set; } = [];

    /// <summary>
    /// Default destination for "retry budget exhausted" alerts, used by every group that does not
    /// override it. Null means no alert unless a group or a subscription+group override defines one.
    /// </summary>
    public string AlertHandlerId { get; set; }

    /// <summary>That adapter's own settings — api key, recipients, subject.</summary>
    public IReadOnlyDictionary<string, string> AlertHandlerProperties { get; set; }
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }
}