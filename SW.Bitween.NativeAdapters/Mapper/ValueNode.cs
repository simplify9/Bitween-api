using System.Diagnostics;

namespace SW.Bitween.NativeAdapters.Mapper;

/// <summary>
/// A document, in the only three shapes JSON and XML have in common: a named set of children, an
/// ordered list, or a single value.
/// </summary>
/// <remarks>
/// <para>
/// This is what the mapper reads from and writes to, so that mapping itself knows nothing about
/// either format. A reader turns a document into one of these; a writer turns one back into text.
/// </para>
/// <para>
/// Values stay as <see cref="string"/>, <see cref="decimal"/>, <see cref="bool"/> or null right up
/// until a writer serialises them - never as text. That is the whole reason this type exists: the
/// old mapper rendered a text template and then had to quote, escape and repair the result, and
/// escaping is only a problem for something that has already become text.
/// </para>
/// </remarks>
[DebuggerDisplay("{Kind}")]
public abstract class ValueNode
{
    public abstract ValueNodeKind Kind { get; }

    public static ValueNode Value(object? value) => new ScalarNode(value);
    public static ObjectNode Object() => new();
    public static ListNode List() => new();
}

public enum ValueNodeKind
{
    Scalar,
    Object,
    List,
}

/// <summary>A single value: string, number, boolean, or null.</summary>
public sealed class ScalarNode(object? value) : ValueNode
{
    public override ValueNodeKind Kind => ValueNodeKind.Scalar;

    /// <summary>Always null, a <see cref="string"/>, a <see cref="decimal"/> or a <see cref="bool"/>.</summary>
    public object? Value { get; } = value;
}

/// <summary>
/// A named set of children, in insertion order.
/// </summary>
/// <remarks>
/// Order is kept because XML cares about it - an XSD sequence is only valid in the order it declares
/// - and because a JSON document whose keys come out in a different order every run is unpleasant to
/// diff. The rules decide the order; nothing infers it from a sample document.
/// </remarks>
public sealed class ObjectNode : ValueNode
{
    private readonly Dictionary<string, ValueNode> _children = new();
    private readonly List<string> _order = new();

    public override ValueNodeKind Kind => ValueNodeKind.Object;

    public IReadOnlyList<string> Keys => _order;

    public bool TryGet(string key, out ValueNode? child) => _children.TryGetValue(key, out child);

    public ValueNode? this[string key] => _children.TryGetValue(key, out var child) ? child : null;

    /// <summary>Adds or replaces a child, keeping its original position when replacing.</summary>
    public void Set(string key, ValueNode child)
    {
        if (!_children.ContainsKey(key)) _order.Add(key);
        _children[key] = child;
    }

    /// <summary>
    /// Returns the child object at <paramref name="key"/>, creating it when it is missing.
    /// </summary>
    /// <remarks>
    /// Replaces a scalar sitting in the way, which happens when one rule targets <c>a</c> and
    /// another targets <c>a.b</c>. Last writer wins, and validation is what should catch the
    /// conflict - silently dropping the deeper rule would be worse.
    /// </remarks>
    public ObjectNode GetOrAddObject(string key)
    {
        if (_children.TryGetValue(key, out var existing) && existing is ObjectNode obj) return obj;
        var created = new ObjectNode();
        Set(key, created);
        return created;
    }

    public IEnumerable<KeyValuePair<string, ValueNode>> Children()
    {
        foreach (var key in _order) yield return new(key, _children[key]);
    }
}

/// <summary>An ordered list, one entry per entry its rule produced.</summary>
public sealed class ListNode : ValueNode
{
    private readonly List<ValueNode> _items = new();

    public override ValueNodeKind Kind => ValueNodeKind.List;

    public IReadOnlyList<ValueNode> Items => _items;

    public void Add(ValueNode item) => _items.Add(item);
}
