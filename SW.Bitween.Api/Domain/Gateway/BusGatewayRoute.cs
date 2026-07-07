using System;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain.Gateway;

public class BusGatewayRoute : BaseEntity, IAudited
{
    public BusGateway BusGateway { get; set; }
    public int BusGatewayId { get; set; }
    public Subscription Subscription { get; set; }
    public int SubscriptionId { get; set; }

    // Optional: supplies partner values (__partner__ / {{partner.KEY}}) to the assigned subscription.
    public Partner Partner { get; set; }
    public int? PartnerId { get; set; }

    // Filter over the bound document's promoted properties. Matching runs the assigned subscription.
    public IPropertyMatchSpecification MatchExpression { get; set; }

    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }
}
