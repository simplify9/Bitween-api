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
        /// <summary>An integration that already exists. Exactly one of this and
        /// <see cref="NewIntegration"/> is given.</summary>
        public int? SubscriptionId { get; set; }

        /// <summary>Define the integration here instead of creating it first. It is created
        /// carrying the gateway's own information type, in the same transaction as the route.</summary>
        public InlineIntegrationCreate NewIntegration { get; set; }

        public int? PartnerId { get; set; }
        public IPropertyMatchSpecification MatchExpression { get; set; }
    }

    public class BusGatewayRouteUpdate : BusGatewayRouteCreate
    {
        public int RouteId { get; set; }
    }

    /// <summary>Shared by the two gateway kinds: which integration a link points at.</summary>
    public static class GatewayLinkTarget
    {
        public const string BothGiven = "INTEGRATION_AMBIGUOUS";
        public const string NeitherGiven = "INTEGRATION_REQUIRED";
    }

    public class RemoveRouteRequest
    {
        public int RouteId { get; set; }
    }
}
