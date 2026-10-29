namespace SW.Bitween.Model;

/// <summary>
/// One setting as the settings page sees it: the catalog's definition plus whatever value is in
/// effect. A secret's value never leaves the server - only <see cref="HasValue"/> reveals that
/// one is set.
/// </summary>
public class SettingRow
{
    public string Key { get; set; } = null!;
    public string Section { get; set; } = null!;
    public string Label { get; set; } = null!;

    /// <summary>Optional help text shown beneath the field.</summary>
    public string? Description { get; set; }

    /// <summary>"string", "number", "boolean" or "color".</summary>
    public string Kind { get; set; } = null!;

    /// <summary>The product default - what a reset returns this setting to. Empty for secrets.</summary>
    public string DefaultValue { get; set; } = null!;

    /// <summary>The stored value. Always null for secrets, whose value never leaves the server.</summary>
    public string? Value { get; set; }

    public bool Secret { get; set; }

    /// <summary>The only values a string setting takes, for a dropdown. Null for free text.</summary>
    public string[]? Choices { get; set; }

    /// <summary>True when the stored value differs from the product default, i.e. a reset would do something.</summary>
    public bool Overridden { get; set; }

    /// <summary>Whether the effective value is non-empty - lets the UI mask a secret that is set.</summary>
    public bool HasValue { get; set; }

    /// <summary>
    /// Whether the UI may write this setting. False for every environment-owned setting, and for a
    /// secret on an instance with no <c>Bitween:SettingsEncryptionKey</c> - there's nowhere safe to
    /// store that, so it stays configuration-only.
    /// </summary>
    public bool Editable { get; set; }

    /// <summary>
    /// How to render the row: <c>"editable"</c> (stored, changeable), <c>"readonly"</c> (an
    /// environment value, shown but not changeable) or <c>"presence"</c> (an environment value
    /// reported only as set or not set).
    /// </summary>
    public string Access { get; set; } = null!;
}

public class SettingUpdate
{
    /// <summary>The new value as text; empty clears the setting. Reset-to-default is a DELETE instead.</summary>
    public string Value { get; set; } = null!;
}
