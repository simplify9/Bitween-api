using SW.PrimitiveTypes;
using System.Collections.Generic;

namespace SW.Bitween.Model
{
    public class ApiGatewayCreate : IName
    {
        public string Name { get; set; }
        public string UrlName { get; set; }

        /// <summary>Off but kept, with its partner attachments. Calls to it are refused.</summary>
        public bool Inactive { get; set; }
    }

    public class ApiGatewayRow : ApiGatewayUpdate
    {
        public int Id { get; set; }
        public int? PartnersCount { get; set; }
    }

    public class ApiGatewayUpdate : ApiGatewayCreate
    {
        public ICollection<ApiGatewayPartnerDto> Partners { get; set; }
    }

    public class ApiGatewayPartnerDto
    {
        public int PartnerId { get; set; }
        public int SubscriptionId { get; set; }
        public string PartnerName { get; set; }
        public string SubscriptionName { get; set; }
    }

    public class ApiGatewayPartnerCreate
    {
        public int PartnerId { get; set; }

        /// <summary>An integration that already exists. Exactly one of this and
        /// <see cref="NewIntegration"/> is given.</summary>
        public int? SubscriptionId { get; set; }

        /// <summary>Define the integration here instead of creating it first. It is created as a
        /// GatewayApiCall in the same transaction as the attachment.</summary>
        public InlineIntegrationCreate NewIntegration { get; set; }
    }

    public class SearchApiGatewayAttachmentsModel
    {
        public int ApiGatewayId { get; set; }
        public string Search { get; set; }
        public int? Offset { get; set; }
        public int? Limit { get; set; }
    }
}

