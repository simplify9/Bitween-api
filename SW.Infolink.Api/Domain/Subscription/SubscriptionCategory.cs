using System;
using SW.PrimitiveTypes;

namespace SW.Infolink.Domain;

public class SubscriptionCategory : BaseEntity<int>, IAudited
{
    private SubscriptionCategory()
    {
    }

    public SubscriptionCategory(string code, string description)
    {
        Code = code;
        Description = description;
    }

    public string Code { get; set; }
    public string Description { get; set; }

    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }

    public void Update(string code, string description)
    {
        Code = code;
        Description = description;
    }
}