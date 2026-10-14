using System.Xml;
using System.Xml.Linq;

namespace SW.Bitween.NativeAdapters.Mapper.Formats;

/// <summary>
/// Reads and writes XML.
/// </summary>
/// <remarks>
/// <para>
/// XML has no arrays, no types and no distinction between an attribute and a child element, so
/// reading it into the neutral tree needs conventions. They are the ones a reader of the source
/// tree can guess: an attribute is its name with <c>@</c> in front, text sitting alongside child
/// elements is <c>#text</c>, and a name that appears more than once becomes a list.
/// </para>
/// <para>
/// Namespaces are dropped on the way in and matched on the local name alone. Real documents bind
/// the same URI to a prefix and to the default at once, un-declare it again further down with
/// <c>xmlns=""</c>, and pick a different prefix for the same namespace in the very next element -
/// so a path that named a prefix would be naming something the partner is free to change.
/// </para>
/// </remarks>
public class XmlFormat : IDocumentFormat
{
    /// <summary>The key holding an element's own text when it also has attributes or children.</summary>
    public const string TextKey = "#text";

    /// <summary>Prefix marking a key that came from an attribute rather than a child element.</summary>
    public const string AttributePrefix = "@";

    public string Id => "xml";

    public string ContentType => "application/xml";

    /// <summary>
    /// A single value counts as a list of one.
    /// </summary>
    /// <remarks>
    /// XML repeats a name to make a list, so a document with one <c>&lt;line&gt;</c> is
    /// indistinguishable from one that is not a list at all. Without this, an order that happened to
    /// have a single line would map to no lines - the line silently gone, and nothing to say so.
    /// </remarks>
    public bool SingleValueIsAList => true;

    /// <summary>How deep a document may nest before it is refused.</summary>
    /// <remarks>
    /// <see cref="FromElement"/> recurses once per element, and a document nested thousands deep
    /// would exhaust the stack - which no <c>catch</c> can recover from, so the process goes with
    /// it. <see cref="XmlReaderSettings"/> has no nesting limit of its own to lean on. 64 is what
    /// Newtonsoft applies to JSON, so both formats refuse the same shape, and it is far past
    /// anything a real document reaches: a SOAP envelope carrying an order gets to about ten.
    /// </remarks>
    private const int MaxDepth = 64;

    public ValueNode Read(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new DocumentFormatException("The document is empty, so there is nothing to map.");

        XDocument document;
        try
        {
            // A partner's document is untrusted input, so no DTD and no resolver: between them
            // they are what lets an XML document declare an entity that expands to gigabytes, or
            // one that reads a file off this server and sends it wherever the mapping writes it.
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            };
            using var reader = XmlReader.Create(new StringReader(text), settings);
            PayloadParseTally.Parsed();
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new DocumentFormatException($"The document is not valid XML: {ex.Message}");
        }

        if (document.Root is null)
            throw new DocumentFormatException("The document has no root element.");

        // The root element is a named key rather than the tree itself, so its name is part of every
        // path - `Envelope.Body.shipping.height`. It is the one name in an XML document that says
        // what the document is, and a mapping that never mentions it would be harder to read.
        var root = ValueNode.Object();
        root.Set(document.Root.Name.LocalName, FromElement(document.Root, 1));
        return root;
    }

    /// <summary>
    /// Writes the tree out, turning what the writer refuses into something the caller can report.
    /// </summary>
    /// <remarks>
    /// The shapes XML cannot hold are caught by name in <see cref="RootElement"/> and
    /// <see cref="BuildElement"/>, but a <em>name</em> is only checked when it is built: a rule
    /// writing to a field called <c>order ref</c> reaches <see cref="XName"/>, which objects with an
    /// <see cref="XmlException"/>, and an <c>@xmlns:x</c> mapped from a field that turned out empty
    /// reaches an <see cref="ArgumentException"/>. Both are the mapping's fault rather than the
    /// server's, and the pipeline already knows how to report a
    /// <see cref="DocumentFormatException"/>.
    /// </remarks>
    public string Write(ValueNode root)
    {
        try
        {
            return new XDocument(RootElement(root)).ToString();
        }
        catch (Exception ex) when (ex is XmlException or ArgumentException)
        {
            throw new DocumentFormatException(
                $"The mapping produced something XML cannot be written from: {ex.Message}");
        }
    }

    /// <summary>
    /// The single element an XML document is allowed to have at the top.
    /// </summary>
    /// <remarks>
    /// JSON's output is a set of top-level keys, or a list; XML has exactly one root element and no
    /// way to say otherwise. So the shapes that cannot be written are refused by name here rather
    /// than producing a document a partner's parser rejects with a stack trace.
    /// </remarks>
    private static XElement RootElement(ValueNode root)
    {
        if (root is not ObjectNode obj)
            throw new DocumentFormatException(
                "An XML document is one root element, so the whole output cannot be a list. " +
                "Write the list inside a named field instead.");

        if (obj.Keys.Count != 1)
            throw new DocumentFormatException(
                obj.Keys.Count == 0
                    ? "The mapping produced nothing, and an XML document needs a root element."
                    : "An XML document has exactly one root element, but the mapping writes " +
                      $"{obj.Keys.Count} at the top level ({string.Join(", ", obj.Keys)}). " +
                      "Put them inside one named field.");

        var name = obj.Keys[0];
        var child = obj[name];

        if (child is ListNode)
            throw new DocumentFormatException(
                $"'{name}' is the root element and a list, and XML cannot repeat the root. " +
                "Put the list inside it.");

        return BuildElement(name, child, NamespaceScope.Empty);
    }

    /// <summary>
    /// Builds one element, resolving its name and its children against the namespaces in scope.
    /// </summary>
    private static XElement BuildElement(string name, ValueNode? node, NamespaceScope scope)
    {
        // Declarations first: an element's own name may use a prefix the same element declares,
        // which is exactly what a SOAP envelope does on its very first line.
        if (node is ObjectNode obj) scope = scope.Extend(obj);

        var element = new XElement(ResolveName(name, scope, isAttribute: false));

        switch (node)
        {
            case ObjectNode content:
                foreach (var (key, child) in content.Children()) Add(element, key, child, scope);
                break;

            // A scalar is the element's text. Null and "" both give `<x/>`, which is the same
            // document either way - XML has no null, and an element with no text is what it has.
            case ScalarNode scalar:
                var text = AsText(scalar.Value);
                if (text.Length > 0) element.Add(new XText(text));
                break;

            case ListNode:
                throw new DocumentFormatException(
                    $"'{name}' is a list inside a list, and XML has no way to write one. " +
                    "Give the inner list its own named field.");
        }

        return element;
    }

    /// <summary>Adds one child of an object: an attribute, its own text, an element, or a list of them.</summary>
    private static void Add(XElement parent, string key, ValueNode child, NamespaceScope scope)
    {
        if (key.StartsWith(AttributePrefix, StringComparison.Ordinal))
        {
            var name = key[AttributePrefix.Length..];

            // Written as declarations on the element itself, so the prefixes in the document are the
            // ones the mapping asked for. Left to itself the writer invents `p1`, `p2` and so on.
            if (IsNamespaceKey(name))
            {
                AddDeclaration(parent, name, AsText((child as ScalarNode)?.Value));
                return;
            }

            parent.SetAttributeValue(ResolveName(name, scope, isAttribute: true),
                AsText((child as ScalarNode)?.Value));
            return;
        }

        if (key == TextKey)
        {
            parent.Add(new XText(AsText((child as ScalarNode)?.Value)));
            return;
        }

        // A list is the same name repeated, which is the only list XML has.
        if (child is ListNode list)
        {
            foreach (var item in list.Items) parent.Add(BuildElement(key, item, scope));
            return;
        }

        parent.Add(BuildElement(key, child, scope));
    }

    /// <summary>
    /// Writes an <c>xmlns</c> declaration, or leaves it out when it un-declares the default.
    /// </summary>
    /// <remarks>
    /// <c>xmlns=""</c> puts a subtree back into no namespace, and real documents do it for every
    /// section of a request body. It cannot be written as an attribute - the writer objects - but it
    /// does not need to be: an element built with no namespace under a parent that has one makes the
    /// writer emit <c>xmlns=""</c> itself.
    /// </remarks>
    private static void AddDeclaration(XElement element, string name, string uri)
    {
        var isDefault = name == "xmlns";

        if (isDefault && uri.Length == 0) return;

        element.SetAttributeValue(
            isDefault ? "xmlns" : XNamespace.Xmlns + name["xmlns:".Length..],
            uri);
    }

    private static bool IsNamespaceKey(string name) =>
        name == "xmlns" || name.StartsWith("xmlns:", StringComparison.Ordinal);

    /// <summary>
    /// Resolves a written name against the prefixes in scope.
    /// </summary>
    /// <remarks>
    /// An unprefixed attribute is in no namespace even where a default namespace is in scope - the
    /// one place XML treats elements and attributes differently, and the reason this takes a flag.
    /// </remarks>
    private static XName ResolveName(string name, NamespaceScope scope, bool isAttribute)
    {
        var colon = name.IndexOf(':');

        if (colon < 0)
        {
            var fallback = isAttribute ? "" : scope.Default;
            return fallback.Length == 0 ? name : XNamespace.Get(fallback) + name;
        }

        var prefix = name[..colon];
        if (!scope.TryGet(prefix, out var uri))
            throw new DocumentFormatException(
                $"'{name}' uses the prefix '{prefix}:', which nothing declares. Add an " +
                $"'@xmlns:{prefix}' field holding its namespace to this element or one above it.");

        return XNamespace.Get(uri) + name[(colon + 1)..];
    }

    /// <summary>
    /// A value as the text of an element or attribute.
    /// </summary>
    /// <remarks>
    /// Invariant throughout: a decimal written under a French locale would use a comma, and the
    /// partner reading it is not in France. A decimal keeps the scale it was given, so a weight of
    /// <c>0.940</c> is written back as <c>0.940</c>. Booleans are lower case, which is what XML
    /// Schema calls a boolean and what every parser expects.
    /// </remarks>
    private static string AsText(object? value) => value switch
    {
        null => "",
        bool b => b ? "true" : "false",
        decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
        string s => s,
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>
    /// The prefixes in scope at one point in the document, and the default namespace.
    /// </summary>
    /// <remarks>
    /// Immutable and copied on the way down, because a declaration applies to the element that made
    /// it and everything inside it, and nothing outside. A document that declares the same prefix
    /// differently in two branches is legal, and this is what keeps the two apart.
    /// </remarks>
    private readonly struct NamespaceScope
    {
        private readonly Dictionary<string, string>? _prefixes;

        private NamespaceScope(Dictionary<string, string>? prefixes, string @default)
        {
            _prefixes = prefixes;
            Default = @default;
        }

        public static NamespaceScope Empty => new(null, "");

        /// <summary>The namespace an unprefixed element name is in. Empty means none.</summary>
        public string Default { get; }

        public bool TryGet(string prefix, out string uri)
        {
            uri = "";
            return _prefixes is not null && _prefixes.TryGetValue(prefix, out uri!);
        }

        /// <summary>The same scope plus whatever declarations this object carries.</summary>
        public NamespaceScope Extend(ObjectNode node)
        {
            Dictionary<string, string>? prefixes = null;
            var @default = Default;

            foreach (var (key, child) in node.Children())
            {
                if (!key.StartsWith(AttributePrefix, StringComparison.Ordinal)) continue;

                var name = key[AttributePrefix.Length..];
                if (!IsNamespaceKey(name)) continue;

                var uri = AsText((child as ScalarNode)?.Value);
                if (name == "xmlns")
                {
                    @default = uri;
                    continue;
                }

                prefixes ??= _prefixes is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(_prefixes, StringComparer.Ordinal);
                prefixes[name["xmlns:".Length..]] = uri;
            }

            return prefixes is null && @default == Default
                ? this
                : new NamespaceScope(prefixes ?? _prefixes, @default);
        }
    }

    /// <summary>
    /// Turns one element into a scalar when it is only text, and an object otherwise.
    /// </summary>
    private static ValueNode FromElement(XElement element, int depth)
    {
        if (depth > MaxDepth)
            throw new DocumentFormatException(
                $"The document nests elements more than {MaxDepth} deep, which is further than " +
                "this mapper will read.");

        var attributes = element.Attributes().Where(IsData).ToList();
        var children = element.Elements().ToList();

        // Nothing but text: the element is its value. `<x/>` and `<x></x>` are the same document, so
        // both read as "" - present and empty, which is what the element says, and distinguishable
        // from a missing element, which reads as nothing at all.
        if (attributes.Count == 0 && children.Count == 0)
            return ValueNode.Value(element.Value);

        var node = ValueNode.Object();

        foreach (var attribute in attributes)
            node.Set(AttributePrefix + attribute.Name.LocalName, ValueNode.Value(attribute.Value));

        // Grouped by local name, in the order each name first appears. A name used more than once
        // is a list; used once, it is the value itself.
        foreach (var group in children.GroupBy(c => c.Name.LocalName))
        {
            var occurrences = group.ToList();
            if (occurrences.Count == 1)
            {
                node.Set(group.Key, FromElement(occurrences[0], depth + 1));
                continue;
            }

            var list = ValueNode.List();
            foreach (var occurrence in occurrences) list.Add(FromElement(occurrence, depth + 1));
            node.Set(group.Key, list);
        }

        // Text alongside children - mixed content. Rare in the documents a partner sends, but
        // dropping it would lose data with no error, which is the one outcome to avoid.
        var text = string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value));
        if (!string.IsNullOrWhiteSpace(text)) node.Set(TextKey, ValueNode.Value(text));

        return node;
    }

    /// <summary>
    /// Whether an attribute carries data rather than declaring a namespace.
    /// </summary>
    /// <remarks>
    /// <c>xmlns</c> and <c>xmlns:*</c> are how XML says what a prefix means, and since paths never
    /// name a prefix they are machinery rather than content. Keeping them would put four of them at
    /// the top of a SOAP document's tree, above the fields anyone is looking for.
    /// </remarks>
    private static bool IsData(XAttribute attribute) => !attribute.IsNamespaceDeclaration;
}
