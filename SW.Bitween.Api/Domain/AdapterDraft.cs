using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;

/// <summary>
/// An adapter being written in Bitween's code editor: its files, until they are published as a
/// version. Python and JavaScript/TypeScript only — they build from their source with nothing but
/// their runtime, which the image has; .NET and Go adapters are built with the CLI.
/// </summary>
public class AdapterDraft : BaseEntity, IAudited
{
    public static readonly string[] Languages = ["python", "node", "typescript"];

    /// <summary>What a draft may hold, so a draft can't become a way to store anything in the database.</summary>
    public const int MaxFiles = 200;
    public const int MaxBytes = 2 * 1024 * 1024;

    private AdapterDraft()
    {
    }

    public AdapterDraft(string adapterId, string language, string kind, string baseVersion, IDictionary<string, string> files)
    {
        if (string.IsNullOrWhiteSpace(adapterId)) throw new ArgumentException("A draft needs an adapter id.", nameof(adapterId));
        if (!Languages.Contains(language)) throw new SWValidationException("Language", $"'{language}' isn't a language the editor builds: {string.Join(", ", Languages)}.");
        AdapterId = adapterId;
        Language = language;
        Kind = kind;
        BaseVersion = baseVersion;
        SetFiles(files);
    }

    public string AdapterId { get; private set; }
    public string Language { get; private set; }
    public string Kind { get; private set; }

    /// <summary>The version it was started from or last published as; null for a new adapter not yet published.</summary>
    public string BaseVersion { get; private set; }

    /// <summary>The files as JSON, path to content. Kept out of the audit trail; <see cref="FilesHash"/> is in it.</summary>
    public string FilesJson { get; private set; }

    /// <summary>SHA-256 of the files, so the trail shows every save without holding the code twice.</summary>
    public string FilesHash { get; private set; }

    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }

    public Dictionary<string, string> Files =>
        JsonConvert.DeserializeObject<Dictionary<string, string>>(FilesJson ?? "{}") ?? new Dictionary<string, string>();

    public void SetFiles(IDictionary<string, string> files)
    {
        if (files == null || files.Count == 0) throw new SWValidationException("Files", "A draft needs its files.");
        if (files.Count > MaxFiles) throw new SWValidationException("Files", $"A draft holds at most {MaxFiles} files.");
        foreach (var path in files.Keys)
            if (!IsSafePath(path)) throw new SWValidationException("Files", $"'{path}' isn't a path inside the adapter.");
        if (files.Values.Sum(v => Encoding.UTF8.GetByteCount(v ?? "")) > MaxBytes)
            throw new SWValidationException("Files", $"A draft holds at most {MaxBytes / (1024 * 1024)} MB of code.");

        var sorted = new SortedDictionary<string, string>(files.ToDictionary(f => f.Key, f => f.Value ?? ""), StringComparer.Ordinal);
        FilesJson = JsonConvert.SerializeObject(sorted);
        FilesHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FilesJson))).ToLowerInvariant();
    }

    public void Published(string version) => BaseVersion = version;

    /// <summary>Relative, forward slashes, nothing that climbs out or hides: what a project folder holds.</summary>
    public static bool IsSafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 300) return false;
        if (path.StartsWith('/') || path.Contains('\\') || path.Contains(':') || path.Contains('\0')) return false;
        var segments = path.Split('/');
        return segments.All(s => s.Length > 0 && s != "." && s != "..");
    }
}

/// <summary>A version published or made current from Bitween: in the audit trail, with who did it.</summary>
public class AdapterRelease
{
    public const string PublishedAction = "published";
    public const string PromotedAction = "promoted";

    private AdapterRelease()
    {
    }

    public AdapterRelease(string adapterId, string version, string action, int? draftId, string accountId)
    {
        AdapterId = adapterId;
        Version = version;
        Action = action;
        DraftId = draftId;
        AccountId = accountId;
        OccurredOn = DateTime.UtcNow;
    }

    public long Id { get; private set; }
    public string AdapterId { get; private set; }
    public string Version { get; private set; }
    public string Action { get; private set; }
    public int? DraftId { get; private set; }
    public string AccountId { get; private set; }
    public DateTime OccurredOn { get; private set; }
}
