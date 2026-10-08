using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using SW.Serverless;

namespace SW.Bitween.Services.DataSources;

/// <summary>
/// What each data source provider accepts, read out of the adapter itself.
///
/// The front end used to carry this list by hand — which fields a RabbitMQ data source starts with,
/// what DeclareMode allows, which values are credentials. A hand-written copy of someone else's
/// contract drifts: the form advertised a "Tls" field for months that the adapter never read, so an
/// operator could tick it, see nothing wrong, and still be authenticating in the clear.
///
/// So the adapter declares its own settings (<c>AdapterSettingsAttribute</c>) and this reads them.
/// Two properties of how it reads matter:
///
/// - The assembly is inspected with <see cref="MetadataLoadContext"/>, never loaded. Nothing in the
///   adapter runs. That is what makes describing safe for a resident bus provider: asking a running
///   one would mean an extra connection to a customer's broker, and asking a stopped one would mean
///   starting a connection nobody asked for.
/// - The attributes are matched by NAME, not by type identity. Adapters build against their own
///   copy of the contract, published on their own cadence, and a type loaded in a metadata
///   context is never reference-equal to the one the host compiled against anyway.
/// </summary>
public class DataSourceProviderCatalog(AdapterInstaller installer, ICloudFilesService cloudFiles,
    ServerlessOptions options, IMemoryCache cache, ILogger<DataSourceProviderCatalog> logger)
{
    /// <summary>
    /// Where Bitween's own providers live. A third-party adapter is found by the Kind stamped on
    /// it at publish time instead; this prefix is what finds the ones published before stamping
    /// existed, and it is a prefilter either way — describing every adapter in the store to build
    /// a menu would mean downloading every adapter in the store.
    /// </summary>
    public const string ConventionPrefix = "bitween.";

    private const string SettingsAttribute = "AdapterSettingsAttribute";
    private const string SettingAttribute = "AdapterSettingAttribute";
    /// <summary>What the installer stamps on an adapter that connects to something.</summary>
    private static readonly string[] ProviderKinds = ["bus", "datasource"];

    /// <summary>
    /// Every data source provider this deployment can offer, described, optionally narrowed to one
    /// DataSourceKind. An adapter that cannot be read is left out rather than throwing: one broken
    /// package must not empty the provider menu.
    /// </summary>
    public async Task<List<DataSourceProviderDescriptor>> ListAsync(string kind = null)
    {
        var described = new List<DataSourceProviderDescriptor>();

        foreach (var adapterId in await CandidatesAsync())
        {
            var descriptor = await DescribeAsync(adapterId);
            if (descriptor == null) continue;

            if (kind != null && !string.Equals(descriptor.Kind, kind, StringComparison.OrdinalIgnoreCase))
                continue;

            described.Add(descriptor);
        }

        return described.OrderBy(p => p.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// One provider, or null when the adapter is missing, unreadable, or declares no settings.
    /// Cached against the package hash, so a republished adapter re-describes itself and an
    /// unchanged one is read from disk once.
    /// </summary>
    public async Task<DataSourceProviderDescriptor> DescribeAsync(string adapterId)
    {
        try
        {
            var installed = await installer.GetMetadataAsync(adapterId);
            var cacheKey = $"bus-provider.{adapterId}.{installed.Hash}";
            if (cache.TryGetValue(cacheKey, out DataSourceProviderDescriptor cached)) return cached;

            // LocalPath is the entry assembly's full path; it exists only once extracted.
            await installer.InstallAsync(adapterId);

            var descriptor = Describe(adapterId, installed.LocalPath);
            if (descriptor == null) return null;

            return cache.Set(cacheKey, descriptor, TimeSpan.FromMinutes(30));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not describe bus provider {AdapterId}; leaving it out of "
                                   + "the catalog.", adapterId);
            return null;
        }
    }

    private async Task<List<string>> CandidatesAsync()
    {
        var root = options.AdapterRemotePath;
        var candidates = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in await cloudFiles.ListAsync($"{root}/"))
        {
            if (item.Size <= 0) continue;

            var adapterId = item.Key.StartsWith($"{root}/", StringComparison.OrdinalIgnoreCase)
                ? item.Key[(root.Length + 1)..]
                : item.Key;

            // Versioned uploads keep the adapter id one segment up from the version.
            if (Semver.IsVersionNumber(adapterId.Split('/').Last()))
                adapterId = string.Join('/', adapterId.Split('/')[..^1]);

            if (adapterId.StartsWith(ConventionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(adapterId);
                continue;
            }

            if (await DeclaresProviderKindAsync(adapterId)) candidates.Add(adapterId);
        }

        return candidates.ToList();
    }

    private async Task<bool> DeclaresProviderKindAsync(string adapterId)
    {
        try
        {
            var metadata = await installer.GetMetadataAsync(adapterId);
            return metadata?.AdapterValues != null &&
                   metadata.AdapterValues.TryGetValue("Kind", out var kinds) &&
                   kinds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                       .Any(k => ProviderKinds.Contains(k, StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads one extracted adapter. Public so a test can point it at a publish directory without
    /// a cloud store in the way.
    /// </summary>
    public static DataSourceProviderDescriptor Describe(string adapterId, string entryAssemblyPath)
    {
        if (!File.Exists(entryAssemblyPath)) return null;

        var directory = Path.GetDirectoryName(entryAssemblyPath)!;
        var assemblies = Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll"))
            .Distinct()
            .ToList();

        using var context = new MetadataLoadContext(new PathAssemblyResolver(assemblies));
        var assembly = context.LoadFromAssemblyPath(entryAssemblyPath);

        foreach (var type in SafeTypes(assembly))
        {
            var marker = AttributeNamed(type.GetCustomAttributesData(), SettingsAttribute);
            if (marker == null) continue;

            return new DataSourceProviderDescriptor
            {
                AdapterId = adapterId,
                Label = Named<string>(marker, "Label") ?? adapterId,
                Kind = Named<string>(marker, "Kind") ?? "Broker",
                Description = Named<string>(marker, "Description"),
                Settings = SettingsOf(type)
            };
        }

        return null;
    }

    private static List<DataSourceProviderSetting> SettingsOf(Type type)
    {
        var settings = new List<DataSourceProviderSetting>();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite) continue;

            var attribute = AttributeNamed(property.GetCustomAttributesData(), SettingAttribute);
            if (attribute != null && Named<bool>(attribute, "Hidden")) continue;

            settings.Add(new DataSourceProviderSetting
            {
                Name = property.Name,
                Type = TypeOf(property.PropertyType),
                Hint = attribute == null ? null : Named<string>(attribute, "Hint"),
                Default = attribute == null ? null : Named<string>(attribute, "Default"),
                AllowedValues = attribute == null ? null : NamedArray(attribute, "AllowedValues"),
                Secret = attribute != null && Named<bool>(attribute, "Secret"),
                Required = attribute != null && Named<bool>(attribute, "Required")
            });
        }

        return settings;
    }

    /// <summary>
    /// What kind of input this is. Deliberately coarse: the UI needs to choose between a text box,
    /// a number, a toggle and a menu, and anything finer would be the UI knowing about brokers
    /// again.
    /// </summary>
    private static string TypeOf(Type type)
    {
        var name = type.FullName ?? type.Name;

        if (name == typeof(bool).FullName) return DataSourceProviderSetting.BooleanType;

        return name is not null && (
            name == typeof(int).FullName || name == typeof(long).FullName ||
            name == typeof(short).FullName || name == typeof(ushort).FullName ||
            name == typeof(uint).FullName || name == typeof(ulong).FullName ||
            name == typeof(byte).FullName || name == typeof(double).FullName ||
            name == typeof(decimal).FullName)
            ? DataSourceProviderSetting.NumberType
            : DataSourceProviderSetting.StringType;
    }

    private static CustomAttributeData AttributeNamed(IEnumerable<CustomAttributeData> attributes, string name) =>
        attributes.FirstOrDefault(a =>
            string.Equals(a.AttributeType.Name, name, StringComparison.Ordinal));

    private static T Named<T>(CustomAttributeData attribute, string name)
    {
        var argument = attribute.NamedArguments
            .FirstOrDefault(a => string.Equals(a.MemberName, name, StringComparison.Ordinal));

        return argument.TypedValue.Value is T value ? value : default;
    }

    private static string[] NamedArray(CustomAttributeData attribute, string name)
    {
        var argument = attribute.NamedArguments
            .FirstOrDefault(a => string.Equals(a.MemberName, name, StringComparison.Ordinal));

        return argument.TypedValue.Value is IEnumerable<CustomAttributeTypedArgument> values
            ? values.Select(v => v.Value as string).Where(v => v != null).ToArray()
            : null;
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
        catch { return []; }
    }
}
