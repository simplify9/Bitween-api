using System;
using System.Collections.Generic;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;

public class GlobalAdapterValuesSet:BaseEntity<string>
{
    public string Name { get; set; }
    public Dictionary<string, string> Values { get; set; }

    /// <summary>
    /// Names within <see cref="Values"/> whose values are never returned by the API in clear.
    /// A set is shared across every adapter that references it, so one secret in a set must not
    /// force the whole set out of sight — the flag is per value, not per set.
    /// </summary>
    public List<string> SecretProperties { get; set; } = new();
}