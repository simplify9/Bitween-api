using System;

namespace SW.Bitween.NativeAdapters;

/// <summary>
/// Marks a native adapter input property as sensitive (e.g. passwords, API keys).
/// Properties with this attribute will have their values omitted from API responses.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SecureAttribute : Attribute
{
}
