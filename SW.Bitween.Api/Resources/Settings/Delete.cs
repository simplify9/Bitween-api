using System;
using System.Threading.Tasks;
using SW.Bitween.Services;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Settings;

/// <summary>
/// Resets one setting to the product default - the value the options class ships with.
/// <para>
/// The row is rewritten rather than deleted: a missing row is how startup recognises a key it has
/// never imported, so dropping it would let configuration seep back in on the next boot. Reset
/// therefore means "stop choosing", not "forget this key exists".
/// </para>
/// </summary>
public class Delete(
    BitweenDbContext dbContext,
    RequestContext requestContext,
    SettingsService settings,
    IInfolinkCache cache,
    IServiceProvider serviceProvider) : IDeleteHandler<string, object>
{
    public async Task<object> Handle(string key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Settings.Edit);

        var definition = SettingsCatalog.Find(key)
                         ?? throw new SWValidationException("SETTING_NOT_FOUND", $"'{key}' is not a known setting.");

        if (!definition.Stored)
            throw new SWValidationException("SETTING_NOT_EDITABLE",
                $"{definition.Label} comes from this instance's configuration, so there's nothing to reset.");

        if (!settings.CanStore(definition))
            throw new SWValidationException("SETTING_ENCRYPTION_UNAVAILABLE",
                $"{definition.Label} is a secret that isn't stored, so there's nothing to reset.");

        // Resetting an already-default setting is a no-op, not an error - the UI can fire this for
        // a staged reset it never saved a value for. Like an update, it clears what it Clears.
        await settings.Save(dbContext, definition, SettingsService.DefaultOf(definition));

        // A reset has to take effect the same way a write does - see Update.
        if (definition.OnChange is not null) await definition.OnChange(serviceProvider);

        await cache.BroadcastRevoke();

        return null;
    }
}
