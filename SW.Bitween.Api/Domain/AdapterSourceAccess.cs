using System;

namespace SW.Bitween.Domain;

/// <summary>
/// Someone read an adapter's source in Bitween. Kept as a row, rather than only logged, so the
/// audit trail records it as it records every other change: who, which adapter and version,
/// which file, and when. Source is a partner's or our own code, and reading it is worth knowing.
/// </summary>
public class AdapterSourceAccess
{
    private AdapterSourceAccess()
    {
    }

    public AdapterSourceAccess(string adapterId, string version, string path, string accountId)
    {
        AdapterId = adapterId;
        Version = version;
        Path = path;
        AccountId = accountId;
        OccurredOn = DateTime.UtcNow;
    }

    public long Id { get; private set; }
    public string AdapterId { get; private set; }

    /// <summary>The version read; empty for a package published without one.</summary>
    public string Version { get; private set; }

    /// <summary>The file read, relative to the package's source folder; null when only the list was.</summary>
    public string Path { get; private set; }

    public string AccountId { get; private set; }
    public DateTime OccurredOn { get; private set; }
}
