using System.Collections.Generic;

namespace SW.Bitween.Resources.AdapterDrafts;

public class AdapterDraftCreate
{
    /// <summary>A new adapter: its name, language and kind, and optionally its id.</summary>
    public string Name { get; set; }
    public string Language { get; set; }
    public string Kind { get; set; }
    public string AdapterId { get; set; }

    /// <summary>Or a published version to start from: set both, and leave the rest.</summary>
    public string FromAdapterId { get; set; }
    public string FromVersion { get; set; }
}

public class AdapterDraftUpdate
{
    public Dictionary<string, string> Files { get; set; }
}

/// <summary>Settings to build, check or try the draft with. Used for the call and never stored.</summary>
public class AdapterDraftRun
{
    public Dictionary<string, string> Settings { get; set; }

    /// <summary>Build only, without the contract checks.</summary>
    public bool BuildOnly { get; set; }

    /// <summary>For try: the command, and its input as JSON or text.</summary>
    public string Command { get; set; }
    public string Input { get; set; }
}

public class AdapterDraftPublish
{
    /// <summary>major, minor, patch, or an explicit version; patch when empty.</summary>
    public string Version { get; set; }
    public string ReleaseNotes { get; set; }
    public Dictionary<string, string> Settings { get; set; }
}
