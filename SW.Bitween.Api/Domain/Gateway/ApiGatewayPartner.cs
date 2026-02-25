using System;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain.Gateway;

public class ApiGatewayPartner: IAudited
{
    public ApiGateway ApiGateway { get; set; }
    public int ApiGatewayId { get; set; }
    public Partner Partner { get; set; }
    public int PartnerId { get; set; }
    public Subscription Subscription { get; set; }
    public int SubscriptionId { get; set; }
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }
}