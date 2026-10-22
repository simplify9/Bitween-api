namespace SW.Bitween.Services.Mirror;

/// <summary>
/// Whether this Bitween is a staging that mirrors a prod's setup, with every value faked, to rehearse
/// an upgrade on.
/// </summary>
public class MirrorOptions
{
    /// <summary>
    /// Set by the deployment, never from the UI: whether this Bitween may be overwritten by a copy of
    /// another. A copy replaces the whole setup, so a prod - which would never set this - can't be
    /// overwritten by a click, whatever its settings say.
    /// </summary>
    public bool Target { get; set; }
}
