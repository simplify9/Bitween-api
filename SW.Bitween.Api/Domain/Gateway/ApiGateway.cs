using System;
using System.Collections.Generic;
using SW.Bitween.Model;
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

    /// <summary>
    /// Chosen by the gateway, not by the caller: once there is more than one way in, the gateway
    /// has to say which it speaks before anything is looked up. A JWT gateway refuses partner keys.
    /// </summary>
    public GatewayAuthMethod AuthMethod { get; set; }

    /// <summary>JWT only. See <see cref="ApiGatewayAuthentication"/> for what each one means.</summary>
    public string JwtIssuer { get; set; }
    public string JwtAudience { get; set; }
    public string JwtPartnerClaim { get; set; }

    public ICollection<ApiGatewayPartner> Partners { get; set; }
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }
}