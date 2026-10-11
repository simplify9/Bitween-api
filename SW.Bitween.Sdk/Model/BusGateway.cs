using SW.PrimitiveTypes;
using System.Collections.Generic;

namespace SW.Bitween.Model
{
    public class BusGatewayCreate : IName
    {
        /// <summary>Required; the server rejects a create without it.</summary>
        public string Name { get; set; } = null!;
        public int DocumentId { get; set; }

        /// <summary>Off but kept, with its routes. Messages stop reaching them.</summary>
        public bool Inactive { get; set; }

        /// <summary>
        /// Null is the INTERNAL bus - the only behaviour that existed before, and still the
        /// default. Set it and this gateway is fed by an external broker instead, through the
        /// resident adapter that data source names.
        /// </summary>
        public int? DataSourceId { get; set; }

        /// <summary>
        /// Which queue, topic or subscription on that data source feeds this gateway. Required for
        /// an external gateway; meaningless for the internal bus, where the Document's own
        /// BusMessageTypeName does the routing.
        /// </summary>
        public string? Endpoint { get; set; }

        /// <summary>
        /// Per-gateway overrides passed to the adapter. NOT YET CONSUMED by either bundled
        /// adapter - see BusGateway.EndpointProperties on the entity.
        /// </summary>
        public Dictionary<string, string> EndpointProperties { get; set; } = new();
    }

    public class BusGatewayUpdate : BusGatewayCreate
    {
    }

    public class BusGatewayRow : BusGatewayUpdate
    {
        public int Id { get; set; }
        public string DocumentName { get; set; } = null!;

        /// <summary>Null for an internal gateway, which is what the list column reads.</summary>
        public string? DataSourceName { get; set; }

        /// <summary>Health of the connection behind it, so a broken broker is visible on the gateway.</summary>
        public string? DataSourceState { get; set; }
        public int? RoutesCount { get; set; }
        public ICollection<BusGatewayRouteDto> Routes { get; set; } = [];
    }

    public class BusGatewayRouteDto
    {
        public int Id { get; set; }
        public int SubscriptionId { get; set; }
        public string SubscriptionName { get; set; } = null!;
        public int? PartnerId { get; set; }

        /// <summary>Null alongside a null PartnerId.</summary>
        public string? PartnerName { get; set; }

        /// <summary>Null routes everything on the gateway to this integration.</summary>
        public IPropertyMatchSpecification? MatchExpression { get; set; }
    }

    public class BusGatewayRouteCreate
    {
        /// <summary>An integration that already exists. Exactly one of this and
        /// <see cref="NewIntegration"/> is given.</summary>
        public int? SubscriptionId { get; set; }

        /// <summary>Define the integration here instead of creating it first. It is created
        /// carrying the gateway's own information type, in the same transaction as the route.</summary>
        public InlineIntegrationCreate? NewIntegration { get; set; }

        public int? PartnerId { get; set; }
        public IPropertyMatchSpecification? MatchExpression { get; set; }
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
