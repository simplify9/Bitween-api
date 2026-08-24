using System;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;

/// <summary>
/// One instance-wide setting, keyed by the setting's catalog key (e.g. <c>Theme.PrimaryColor</c>).
/// This table is the single source of truth: configuration seeds a key once — on the first boot
/// after that key exists — and is ignored for it from then on. Every catalog key normally has a
/// row; "reset to default" rewrites the row with the product default rather than removing it.
/// <para>
/// Deliberately a plain key/value store: the definition of a key (label, section, type,
/// whether it's a secret) lives in <see cref="Services.SettingsCatalog"/>, so adding a
/// setting never needs a migration or a data fix-up. A secret's value is encrypted before it
/// gets here — see <see cref="Services.SettingsProtector"/>.
/// </para>
/// </summary>
public class Setting : BaseEntity<string>, IAudited
{
    public string Value { get; set; }

    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
    public string ModifiedBy { get; set; }
}
