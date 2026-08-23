using SW.PrimitiveTypes;
using System.Collections.Generic;

namespace SW.Bitween.Model
{
    public class BusGatewayCreate : IName
    {
        public string Name { get; set; }
        public int DocumentId { get; set; }

        /// <summary>Off but kept, with its routes. Messages stop reaching them.</summary>
        public bool Inactive { get; set; }
    }

    public class BusGatewayUpdate : BusGatewayCreate
    {
    }

    public class BusGatewayRow : BusGatewayUpdate
    {
        public int Id { get; set; }
        public string DocumentName { get; set; }
        public int? RoutesCount { get; set; }
        public ICollection<BusGatewayRouteDto> Routes { get; set; }
    }

    public class BusGatewayRouteDto
    {
        public int Id { get; set; }
        public int SubscriptionId { get; set; }
        public string SubscriptionName { get; set; }
        public int? PartnerId { get; set; }
        public string PartnerName { get; set; }
        public IPropertyMatchSpecification MatchExpression { get; set; }
    }

    public class BusGatewayRouteCreate
    {
        public int SubscriptionId { get; set; }
        public int? PartnerId { get; set; }
        public IPropertyMatchSpecification MatchExpression { get; set; }
    }

    public class BusGatewayRouteUpdate : BusGatewayRouteCreate
    {
        public int RouteId { get; set; }
    }

    public class RemoveRouteRequest
    {
        public int RouteId { get; set; }
    }
}
