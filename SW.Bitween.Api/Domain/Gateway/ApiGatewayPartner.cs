namespace SW.Bitween.Domain.Gateway;

public class ApiGatewayPartner
{
    public ApiGateway ApiGateway { get; set; }
    public int ApiGatewayId { get; set; }
    public Partner Partner { get; set; }
    public int PartnerId { get; set; }
    public Subscription Subscription { get; set; }
    public int? SubscriptionId { get; set; }
}