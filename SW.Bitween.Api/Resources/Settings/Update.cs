using System;
using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.Services;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Settings;

/// <summary>
/// Stores a new value for one setting. It's applied to the live options singletons as well as
/// stored, so the very next request already sees it; the cache-revoke broadcast carries the change
/// to any other instance. Secrets are encrypted before they're written.
/// </summary>
public class Update(
    BitweenDbContext dbContext,
    RequestContext requestContext,
    SettingsService settings,
    IInfolinkCache cache) : ICommandHandler<string, SettingUpdate, object>
{
    public async Task<object> Handle(string key, SettingUpdate request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Settings.Edit);

        var definition = SettingsCatalog.Find(key)
                         ?? throw new SWValidationException("SETTING_NOT_FOUND", $"'{key}' is not a known setting.");

        if (!settings.CanStore(definition))
            throw new SWValidationException("SETTING_ENCRYPTION_UNAVAILABLE",
                $"{definition.Label} is a secret and can only be stored once " +
                $"{BitweenOptions.ConfigurationSection}:{nameof(BitweenOptions.SettingsEncryptionKey)} is configured.");

        // Empty is a real value — it's how you clear an optional link or a license key.
        var value = request?.Value ?? string.Empty;

        try
        {
            SettingsService.Validate(definition, value);
        }
        catch (FormatException ex)
        {
            throw new SWValidationException("SETTING_INVALID_VALUE", $"{definition.Label}: {ex.Message}");
        }

        await Store(definition, value);
        settings.Apply(definition, value);
        await cache.BroadcastRevoke();

        return null;
    }

    /// <summary>
    /// Writes the value, creating the row if startup hasn't imported this key yet. Every setting
    /// keeps exactly one row, keyed by the catalog key.
    /// </summary>
    private async Task Store(SettingDefinition definition, string value)
    {
        var stored = await dbContext.Set<Setting>().FindAsync(definition.Key);
        var toStore = settings.ToStored(definition, value);

        if (stored is null)
            dbContext.Add(new Setting { Id = definition.Key, Value = toStore });
        else
            stored.Value = toStore;

        await dbContext.SaveChangesAsync();
    }
}
