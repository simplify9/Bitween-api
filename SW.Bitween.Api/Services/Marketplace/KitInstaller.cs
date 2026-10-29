using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using SW.Bitween.Cli;
using SW.Bitween.Cli.Configuration;
using SW.PrimitiveTypes;
using SW.Serverless.Contract.Catalog;

namespace SW.Bitween.Services.Marketplace;

/// <summary>A kit's parameter, as the install form asks for it.</summary>
public record KitParameterInfo(string Name, string Description, bool Required, bool Secret, string Default, string Example);

/// <summary>What a kit is and asks for, from its registry listing.</summary>
public record KitInfo(string Id, string Version, string DisplayName, string Summary, string Description,
    IReadOnlyList<KitConnector> Connectors, IReadOnlyList<KitParameterInfo> Parameters);

/// <summary>What planning or installing a kit came to: the plan as bitween kit install prints it, and whether it can go ahead.</summary>
public record KitRun(string Name, bool Ready, bool Applied, IReadOnlyList<string> Lines, IReadOnlyList<string> Problems, IReadOnlyList<string> Warnings, int Changes);

/// <summary>
/// Installs a kit from the registry, as the person asking: the same planner bitween kit install uses,
/// run against this Bitween's own API with the caller's own session, so what an installation can do
/// is exactly what its caller can, and every change is in the audit trail under their name.
/// </summary>
/// <remarks>
/// The kit's package is checked as an adapter's is before anything is read from it: published,
/// not revoked, signed by its publisher and countersigned by a trusted registry key.
/// </remarks>
public class KitInstaller(MarketplaceInstaller marketplace, RegistryClient registry, FileLinks links, IHttpContextAccessor httpContextAccessor)
{
    public async Task<KitInfo> DescribeAsync(string id, string version)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
            throw new SWValidationException("Package", "Say which kit and version.");
        var detail = await registry.VersionAsync(id?.Trim().ToLowerInvariant(), version?.Trim());
        var manifest = AdapterManifest.Parse(detail.Manifest.GetRawText());
        if (!MarketplaceInstaller.IsKit(manifest))
            throw new SWValidationException("REGISTRY_NOT_KIT", $"{detail.PackageId} is an adapter, not a kit.");

        var connectors = new List<KitConnector>();
        var parameters = new List<KitParameterInfo>();
        if (manifest.Extensions?.TryGetValue("kit", out var kit) == true && kit.ValueKind == JsonValueKind.Object)
        {
            if (kit.TryGetProperty("connectors", out var cs) && cs.ValueKind == JsonValueKind.Array)
                connectors.AddRange(cs.EnumerateArray().Select(c => new KitConnector(Text(c, "id"), Text(c, "version"))));
            if (kit.TryGetProperty("parameters", out var ps) && ps.ValueKind == JsonValueKind.Array)
                parameters.AddRange(ps.EnumerateArray().Select(p => new KitParameterInfo(Text(p, "name"), Text(p, "description"),
                    !p.TryGetProperty("required", out var r) || r.ValueKind != JsonValueKind.False,
                    p.TryGetProperty("secret", out var s) && s.ValueKind == JsonValueKind.True,
                    Text(p, "default"), Text(p, "example"))));
        }
        return new KitInfo(detail.PackageId, detail.Version, manifest.DisplayName, manifest.Summary, manifest.Description, connectors, parameters);
    }

    /// <summary>What installing would do, changing nothing.</summary>
    public Task<KitRun> PlanAsync(string id, string version, string name, IDictionary<string, string> parameters) =>
        RunAsync(id, version, name, parameters, apply: false);

    /// <summary>Installs it: its connectors from the marketplace, then its configuration with the parameters filled in.</summary>
    public Task<KitRun> InstallAsync(string id, string version, string name, IDictionary<string, string> parameters) =>
        RunAsync(id, version, name, parameters, apply: true);

    /// <summary>
    /// What installing a kit given as its zip would do, changing nothing: how the registry checks a kit
    /// against a reference Bitween before review. With <paramref name="examples"/>, a required parameter
    /// not given takes the kit's example, or a placeholder, as bitween kit test does.
    /// </summary>
    public async Task<KitRun> PlanPackageAsync(byte[] package, string name, IDictionary<string, string> parameters, bool examples)
    {
        if (package == null || package.Length == 0) throw new SWValidationException("Package", "Send the kit's zip as the request body.");
        return await RunPackageAsync(package, "kit", name, parameters, apply: false, examples);
    }

    async Task<KitRun> RunAsync(string id, string version, string name, IDictionary<string, string> parameters, bool apply)
    {
        var (package, detail, _) = await marketplace.FetchVerifiedAsync(id, version, refuseKits: false);
        return await RunPackageAsync(package, detail.PackageId, name, parameters, apply, examples: false);
    }

    async Task<KitRun> RunPackageAsync(byte[] package, string fallbackName, string name, IDictionary<string, string> parameters,
        bool apply, bool examples)
    {
        var folder = Path.Combine(Path.GetTempPath(), "bitween-kits", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var zip = Path.Combine(folder, "kit.zip");
        await File.WriteAllBytesAsync(zip, package);
        try
        {
            ConfigFile config;
            try
            {
                Kit kit;
                try
                {
                    kit = Kit.Load(zip);
                }
                catch (InvalidDataException)
                {
                    throw new ConfigException("That isn't a zip: send the one bitween kit build writes.");
                }
                var given = (parameters ?? new Dictionary<string, string>())
                    .Where(p => p.Value != null).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                if (examples)
                    foreach (var p in kit.Parameters.Where(p => p.Required && p.Default == null && !given.Keys.Any(k => string.Equals(k, p.Name, StringComparison.OrdinalIgnoreCase))))
                        given[p.Name] = p.Example ?? $"<{p.Name}>";
                config = kit.Configuration(given, null, string.IsNullOrWhiteSpace(name) ? null : name.Trim());
            }
            catch (ConfigException ex)
            {
                return new KitRun(name ?? fallbackName, false, false, [], [ex.Message], [], 0);
            }

            using var api = new BitweenApi(null, null, new Profile { Url = SelfUrl, AccessToken = CallerToken() });
            var ctx = new Context(api, config.SecretValues);
            var planner = new Planner(ctx, config);
            await planner.ReadAsync();
            var plan = await planner.PlanAsync(prune: false);

            using var output = new StringWriter();
            planner.Print(plan, output);
            var ready = plan.Problems.Count == 0;
            var applied = false;
            if (apply && ready)
            {
                output.WriteLine("Installing:");
                try
                {
                    await planner.ApplyAsync(plan, output);
                    applied = true;
                }
                catch (ConfigException ex)
                {
                    output.WriteLine(ex.Message);
                    output.WriteLine("Stopped there; what was done before it stays done. Installing again carries on.");
                }
            }

            return new KitRun(config.Name, ready, applied, Lines(output.ToString()), plan.Problems, plan.Warnings, plan.Changes.Count);
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch { /* temporary */ }
        }
    }

    /// <summary>
    /// This Bitween's own address: where it listens, so the call doesn't go out through the ingress
    /// and back; the address the request came in on when the host doesn't say.
    /// </summary>
    string SelfUrl => string.IsNullOrWhiteSpace(links.InstanceUrl) ? links.BaseUrl : links.InstanceUrl;

    string CallerToken()
    {
        var header = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        return header?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? header[7..].Trim()
            : throw new SWUnauthorizedException();
    }

    static IReadOnlyList<string> Lines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();

    static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()
            : null;
}
