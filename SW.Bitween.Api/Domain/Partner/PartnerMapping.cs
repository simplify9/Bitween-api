using System;
using System.Collections.Generic;
using System.Linq;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;

/// <summary>
/// A partner's own version of a subscription's mapping, made in the partner portal and used in place
/// of the subscription's for that partner's exchanges only. The subscription's own mapping stays as it
/// is: other partners on the same gateway keep it, and removing this puts the partner back on it.
/// </summary>
/// <remarks>
/// Always the native mapper's settings: the rules and the two samples the editor keeps. Picked when
/// an exchange is made for the partner, so it travels with the exchange and its retries like the
/// subscription's mapping does.
/// </remarks>
public class PartnerMapping : BaseEntity, IAudited
{
    private PartnerMapping()
    {
    }

    public PartnerMapping(int subscriptionId, int partnerId, IDictionary<string, string> mapperProperties)
    {
        SubscriptionId = subscriptionId;
        PartnerId = partnerId;
        SetMapperProperties(mapperProperties);
    }

    public int SubscriptionId { get; private set; }
    public int PartnerId { get; private set; }

    public IReadOnlyDictionary<string, string> MapperProperties { get; private set; } = new Dictionary<string, string>();

    public void SetMapperProperties(IDictionary<string, string> mapperProperties) =>
        MapperProperties = (mapperProperties ?? new Dictionary<string, string>()).ToDictionary();

    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }
}
