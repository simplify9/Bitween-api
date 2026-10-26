namespace SW.Bitween.Services.Mirror;

/// <summary>
/// Where this Bitween copies prod from, and whether it may: a staging that mirrors a prod's setup,
/// with every value faked, to rehearse an upgrade on.
/// </summary>
public class MirrorOptions
{
    /// <summary>
    /// Set by the deployment, never from the UI: whether this Bitween may be overwritten by a copy of
    /// another. A copy replaces the whole setup, so a prod - which would never set this - can't be
    /// overwritten by a click, whatever its settings say.
    /// </summary>
    public bool Target { get; set; }

    /// <summary>Prod's address, e.g. https://bitween.acme.com.</summary>
    public string SourceUrl { get; set; }

    /// <summary>A service account token on prod with view-only permissions. A secret.</summary>
    public string SourceToken { get; set; }

    /// <summary>For a prod too old to have service accounts: a member with the Viewer role.</summary>
    public string SourceEmail { get; set; }

    /// <summary>That member's password. A secret.</summary>
    public string SourcePassword { get; set; }

    /// <summary>
    /// Partner properties and global values copied as they are, one per line, because they are
    /// settings rather than data: partner:timeZone, or global:formats/date. Never an address or a secret.
    /// </summary>
    public string KeepRealValues { get; set; }
}
