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
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }
}