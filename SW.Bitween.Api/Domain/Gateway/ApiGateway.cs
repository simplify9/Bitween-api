using System;
using System.Collections.Generic;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain.Gateway;

public class ApiGateway : BaseEntity,IAudited
{
    public string Name { get; set; }
    public string UrlName { get; set; }

    /// <summary>
    /// Turns the gateway off without deleting it. Deleting is the only alternative today,
    /// and it takes the partner attachments with it — so a gateway that needs stopping for
    /// an afternoon gets rebuilt by hand afterwards, or left running.
    /// </summary>
    public bool Inactive { get; set; }
    public ICollection<ApiGatewayPartner> Partners { get; set; }
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }
}