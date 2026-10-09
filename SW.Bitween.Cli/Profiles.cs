using System.Text.Json;
using System.Text.Json.Serialization;

namespace SW.Bitween.Cli;

/// <summary>A Bitween signed in to: where it is, who, and the tokens that keep the session.</summary>
public class Profile
{
    public string Url { get; set; }
    public string Email { get; set; }
    public string AccessToken { get; set; }

    /// <summary>Renews the access token, as the browser's cookie does. Each renewal replaces it.</summary>
    public string RefreshToken { get; set; }

    /// <summary>Accepts any certificate: for a Bitween on a development certificate only.</summary>
    public bool Insecure { get; set; }
}

public class ProfileFile
{
    public string Current { get; set; }
    public Dictionary<string, Profile> Profiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The signed-in Bitweens, kept in the user's own configuration folder, readable by them alone:
/// a refresh token is as good as a password until it is used or expires.
/// </summary>
public class Profiles(string path = null)
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Path { get; } = path ?? Environment.GetEnvironmentVariable("BITWEEN_CLI_CONFIG") ?? DefaultPath();

    static string DefaultPath()
    {
        var root = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
                ? xdg
                : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return System.IO.Path.Combine(root, "bitween", "cli.json");
    }

    public ProfileFile Load()
    {
        if (!File.Exists(Path)) return new ProfileFile();
        try
        {
            var file = JsonSerializer.Deserialize<ProfileFile>(File.ReadAllText(Path), Json) ?? new ProfileFile();
            file.Profiles = new Dictionary<string, Profile>(file.Profiles ?? new(), StringComparer.OrdinalIgnoreCase);
            return file;
        }
        catch (JsonException)
        {
            return new ProfileFile();
        }
    }

    public void Save(ProfileFile file)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file, Json));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, Path, overwrite: true);
    }

    /// <summary>The profile asked for by name, or the current one; null when signed in nowhere.</summary>
    public (string Name, Profile Profile) Find(string name = null)
    {
        var file = Load();
        var wanted = name ?? Environment.GetEnvironmentVariable("BITWEEN_PROFILE") ?? file.Current;
        return wanted != null && file.Profiles.TryGetValue(wanted, out var profile) ? (wanted, profile) : (wanted, null);
    }

    public void Put(string name, Profile profile, bool makeCurrent)
    {
        var file = Load();
        file.Profiles[name] = profile;
        if (makeCurrent || file.Current == null) file.Current = name;
        Save(file);
    }

    public bool Remove(string name)
    {
        var file = Load();
        if (!file.Profiles.Remove(name)) return false;
        if (string.Equals(file.Current, name, StringComparison.OrdinalIgnoreCase)) file.Current = file.Profiles.Keys.FirstOrDefault();
        Save(file);
        return true;
    }
}
