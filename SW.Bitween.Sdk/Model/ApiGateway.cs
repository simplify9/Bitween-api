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
        public int SubscriptionId { get; set; }
    }
}

