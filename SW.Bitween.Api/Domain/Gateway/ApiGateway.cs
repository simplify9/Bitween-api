using System;
using System.Collections.Generic;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain.Gateway;

public class ApiGateway : BaseEntity,IAudited
{
    public string Name { get; set; }
    public string UrlName { get; set; }
    public ICollection<ApiGatewayPartner> Partners { get; set; }
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }
}