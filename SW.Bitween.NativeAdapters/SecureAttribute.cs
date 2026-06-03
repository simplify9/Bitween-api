using System;

namespace SW.Bitween.NativeAdapters;

/// <summary>
/// Marks a native adapter input property as sensitive (e.g. passwords, API keys).
/// Properties with this attribute will have their values replaced with the sentinel
/// string "__private__" in API responses. The real value is never sent to clients,
/// but is preserved in the database and restored automatically when the sentinel is
/// submitted back unchanged.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SecureAttribute : Attribute
{
}
