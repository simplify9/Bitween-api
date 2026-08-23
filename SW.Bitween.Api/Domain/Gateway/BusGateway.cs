using System;
using System.Collections.Generic;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain.Gateway;

public class BusGateway : BaseEntity, IAudited
{
    public string Name { get; set; }
    public int DocumentId { get; set; }

    /// <summary>
    /// Turns the gateway off without deleting it — its routes stop being offered the
    /// message. See <see cref="ApiGateway.Inactive"/>.
    /// </summary>
    public bool Inactive { get; set; }
    public ICollection<BusGatewayRoute> Routes { get; set; }
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }
}
