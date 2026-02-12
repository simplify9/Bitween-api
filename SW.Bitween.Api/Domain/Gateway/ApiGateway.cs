using System.Collections.Generic;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain.Gateway;

public class ApiGateway : BaseEntity
{
    public string Name { get; set; }
    public string UrlName { get; set; }
    public ICollection<ApiGatewayPartner> Partners { get; set; }
}