using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;

namespace SW.Bitween.Services;

/// <summary>
/// Keeps the <see cref="BitweenOptions"/> and <see cref="ThemeOptions"/> singletons in sync with
/// the <c>Settings</c> table, which is the single source of truth for every editable setting.
/// <para>
/// Configuration (env / appsettings) is read <b>once per setting</b>: the first boot after a key
/// exists copies what configuration says into a row (<see cref="ImportMissing"/>), and from then
/// on configuration is ignored for that key. "Default" therefore means the product default —
/// the initializer on the options class — which is what a reset returns a setting to.
/// </para>
/// <para>
/// Applying a value is just assigning the property: both option objects are singletons that every
/// consumer reads per call, so no consumer needs to know settings can change.
/// </para>
/// </summary>
public class SettingsService
{
    /// <summary>Pristine options — never mutated, only read, to answer "what ships in the box?".</summary>
    private static readonly SettingsTarget CodeDefaults = new(new BitweenOptions(), new ThemeOptions());

    private readonly SettingsTarget _target;
    private readonly SettingsProtector _protector;
    private readonly ILogger<SettingsService> _logger;

    /// <summary>
    /// What configuration bound at startup. This is the import source, <b>not</b> the default —
    /// captured before any stored value is applied, and used only for keys with no row yet.
    /// </summary>
    private readonly Dictionary<string, string> _configured;

    public SettingsService(BitweenOptions bitweenOptions, ThemeOptions themeOptions,
        SettingsProtector protector, ILogger<SettingsService> logger)
    {
        _target = new SettingsTarget(bitweenOptions, themeOptions);
        _protector = protector;
        _logger = logger;
        _configured = SettingsCatalog.All.ToDictionary(d => d.Key, d => d.Read(_target) ?? string.Empty);
    }

    /// <summary>The product default: what this setting is without anyone having chosen anything.</summary>
    public static string DefaultOf(SettingDefinition definition) => definition.Read(CodeDefaults) ?? string.Empty;

    /// <summary>
    /// Product defaults for one key prefix, keyed the way the options object serializes
    /// (<c>Theme.LoginLogo</c> → <c>loginLogo</c>). Lets an unauthenticated client tell a brand
    /// value someone chose from one nobody has touched.
    /// </summary>
    public static Dictionary<string, string> DefaultsUnder(string prefix) => SettingsCatalog.All
        .Where(d => d.Key.StartsWith(prefix, StringComparison.Ordinal))
        .ToDictionary(d => Camelize(d.Key[prefix.Length..]), DefaultOf);

    /// <summary>A secret can only be stored if there's a passphrase to protect it with.</summary>
    public bool CanStore(SettingDefinition definition) => !definition.Secret || _protector.IsConfigured;

    /// <summary>Ciphertext for a secret, the value itself for everything else.</summary>
    public string ToStored(SettingDefinition definition, string value) =>
        definition.Secret ? _protector.Protect(value) : value;

    /// <summary>
    /// Copies configuration into a row for any key that doesn't have one yet — the one-time
    /// hand-off that lets an existing deployment keep the values it was configured with. Runs at
    /// startup only.
    /// </summary>
    public async Task ImportMissing(BitweenDbContext dbContext)
    {
        var stored = await dbContext.Set<Setting>().AsNoTracking().Select(s => s.Id).ToListAsync();
        var have = new HashSet<string>(stored, StringComparer.OrdinalIgnoreCase);

        var imported = new List<string>();
        foreach (var definition in SettingsCatalog.All)
        {
            if (have.Contains(definition.Key)) continue;
            // A secret with nowhere safe to go stays in configuration until a passphrase exists.
            if (!CanStore(definition)) continue;

            var configured = _configured.GetValueOrDefault(definition.Key) ?? string.Empty;
            dbContext.Add(new Setting { Id = definition.Key, Value = ToStored(definition, configured) });
            imported.Add(definition.Key);
        }

        if (imported.Count == 0) return;

        try
        {
            await dbContext.SaveChangesAsync();
            _logger.LogInformation("Imported {Count} setting(s) from configuration: {Keys}",
                imported.Count, string.Join(", ", imported));
        }
        catch (DbUpdateException ex)
        {
            // Two instances booting together both try to import; whoever loses just reads the
            // other's rows in Reload. Nothing to repair.
            _logger.LogWarning(ex, "Setting import collided with another instance; using the stored rows.");
            dbContext.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Re-reads every stored setting and applies it. Called once before the app starts serving,
    /// and again on each cache-revoke broadcast so an instance picks up a change made on another.
    /// </summary>
    public async Task Reload(BitweenDbContext dbContext)
    {
        var rows = await dbContext.Set<Setting>().AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Value, StringComparer.OrdinalIgnoreCase);

        foreach (var definition in SettingsCatalog.All)
        {
            // No row means nobody could store this key yet (a secret with no passphrase), so
            // whatever configuration bound at startup stands.
            if (!rows.TryGetValue(definition.Key, out var stored)) continue;

            try
            {
                Assign(definition, definition.Secret ? _protector.Unprotect(stored) : stored);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Stored value for setting {Key} could not be read — it was likely written under a " +
                    "different {Option}. Leaving the running value untouched.",
                    definition.Key, nameof(BitweenOptions.SettingsEncryptionKey));
            }
        }
    }

    /// <summary>Applies one value immediately, so the request that saved it sees the effect.</summary>
    public void Apply(SettingDefinition definition, string value) => Assign(definition, value);

    /// <summary>
    /// Runs the real write against throwaway option objects, so the endpoint can reject "abc"
    /// for a number before it reaches the database — without the live values ever seeing it.
    /// </summary>
    public static void Validate(SettingDefinition definition, string value) =>
        definition.Write(new SettingsTarget(new BitweenOptions(), new ThemeOptions()), value);

    /// <summary>
    /// A bad stored value must never stop the app from booting: log it and leave the key at its
    /// product default.
    /// </summary>
    private void Assign(SettingDefinition definition, string value)
    {
        try
        {
            definition.Write(_target, value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Stored value for setting {Key} could not be applied; falling back to the product default.",
                definition.Key);
            try
            {
                definition.Write(_target, DefaultOf(definition));
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Product default for setting {Key} is itself invalid.", definition.Key);
            }
        }
    }

    private static string Camelize(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
