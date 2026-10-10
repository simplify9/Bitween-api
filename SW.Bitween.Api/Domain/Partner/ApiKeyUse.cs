using System;

namespace SW.Bitween.Domain;

/// <summary>
/// When a partner's API key last got a call through a gateway. Kept beside the key rather than on
/// it, so a gateway call writes one small row instead of the partner; written at most every few
/// minutes per key and node (see <see cref="GatewayActivity"/>), so it is approximate by that much.
/// </summary>
public class ApiKeyUse
{
    private ApiKeyUse()
    {
    }

    public ApiKeyUse(int partnerId, string keyName, DateTime usedOn)
    {
        PartnerId = partnerId;
        KeyName = keyName;
        LastUsedOn = usedOn;
    }

    public int PartnerId { get; private set; }
    public string KeyName { get; private set; }
    public DateTime LastUsedOn { get; set; }
}
