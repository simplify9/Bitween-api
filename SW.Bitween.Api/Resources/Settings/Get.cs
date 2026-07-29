using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.Services;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Settings;

/// <summary>
/// Every editable setting, in catalog order: the definition from <see cref="SettingsCatalog"/>
/// joined with the stored value. Rows normally exist for all of them — startup imports whatever
/// configuration had — so a missing row means the key couldn't be stored yet.
/// </summary>
public class Get(BitweenDbContext dbContext, RequestContext requestContext, SettingsService settings)
    : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Settings.View);

        var rows = await dbContext.Set<Setting>().AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Value, StringComparer.OrdinalIgnoreCase);

        return SettingsCatalog.All.Select(definition =>
        {
            var hasRow = rows.TryGetValue(definition.Key, out var stored);
            var productDefault = SettingsService.DefaultOf(definition);
            // A secret's value is withheld either way round: only whether one is set is public.
            var value = definition.Secret ? null : hasRow ? stored : productDefault;
            // A secret has no product default, and its ciphertext couldn't be compared with one
            // anyway — so for a secret both "is set" and "is overridden" mean the same thing:
            // a non-empty value is stored.
            var secretIsSet = hasRow && !string.IsNullOrEmpty(stored);

            return new SettingRow
            {
                Key = definition.Key,
                Section = definition.Section,
                Label = definition.Label,
                Description = definition.Description,
                Kind = definition.Kind.ToString().ToLowerInvariant(),
                DefaultValue = definition.Secret ? string.Empty : productDefault,
                Value = value,
                Secret = definition.Secret,
                Overridden = definition.Secret ? secretIsSet : hasRow && stored != productDefault,
                HasValue = definition.Secret
                    ? secretIsSet
                    : !string.IsNullOrEmpty(hasRow ? stored : productDefault),
                // The only thing that makes a setting uneditable: a secret with no passphrase
                // configured to protect it.
                Editable = settings.CanStore(definition)
            };
        }).ToArray();
    }
}
