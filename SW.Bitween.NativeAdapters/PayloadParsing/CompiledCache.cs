using System.Security.Cryptography;
using System.Text;

namespace SW.Bitween.NativeAdapters;

/// <summary>
/// Whether the built-in mappers keep what they compile from their settings: Scriban templates and
/// mapping rules.
/// </summary>
/// <remarks>
/// <c>Bitween:CacheCompiledTemplates</c>, on unless set off, copied here at startup because the
/// adapters cannot see the API's options. Off, every call compiles its own, as it always did.
/// </remarks>
public static class CompiledTemplates
{
    public static bool Enabled { get; set; } = true;

    /// <summary>How many compiled templates, and separately mapping rules, are kept.</summary>
    public const int Capacity = 512;
}

/// <summary>
/// What was compiled from a text, kept by a hash of the text, the least recently used dropped past
/// <see cref="capacity"/>. The same text compiles to the same result, so an edit is simply a new entry.
/// </summary>
/// <remarks>
/// What is kept is handed to every caller at once, so it must be something no caller changes: a parsed
/// Scriban template, which renders into a context of its own per call, and mapping rules, which the
/// mapper only reads. Compiling runs outside the lock, so two first calls may both compile; one result
/// is kept and both are correct.
/// </remarks>
internal sealed class CompiledCache<T>(int capacity) where T : class
{
    readonly Dictionary<string, LinkedListNode<(string Key, T Value)>> entries = new(StringComparer.Ordinal);
    readonly LinkedList<(string Key, T Value)> recent = new();
    readonly object gate = new();

    public int Count
    {
        get
        {
            lock (gate) return entries.Count;
        }
    }

    /// <summary>
    /// What <paramref name="compile"/> makes of <paramref name="text"/>, from the cache when it is there.
    /// A result <paramref name="keep"/> refuses, or a compile that throws, is not kept.
    /// </summary>
    public T GetOrAdd(string text, Func<string, T> compile, Func<T, bool>? keep = null)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        lock (gate)
            if (TryTake(key, out var found))
                return found;

        var value = compile(text);
        if (keep != null && !keep(value)) return value;

        lock (gate)
        {
            if (TryTake(key, out var raced)) return raced;
            entries[key] = recent.AddFirst((key, value));
            if (entries.Count > capacity)
            {
                entries.Remove(recent.Last!.Value.Key);
                recent.RemoveLast();
            }
        }

        return value;
    }

    public void Clear()
    {
        lock (gate)
        {
            entries.Clear();
            recent.Clear();
        }
    }

    bool TryTake(string key, out T value)
    {
        if (entries.TryGetValue(key, out var node))
        {
            recent.Remove(node);
            recent.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = null!;
        return false;
    }
}
