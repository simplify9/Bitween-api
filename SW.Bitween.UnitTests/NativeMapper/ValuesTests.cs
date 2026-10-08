using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters.Mapper;
using ValueType = SW.Bitween.NativeAdapters.Mapper.ValueType;

namespace SW.Bitween.UnitTests.NativeMapper;

/// <summary>
/// Type conversion, which the old mapper emitted into a Scriban template as a nest of generated
/// ternaries and so could not test on its own.
/// </summary>
[TestClass]
public class ValuesCoerceTests
{
    /// <summary>
    /// Commas count only as thousands separators in groups of three ahead of the decimal point.
    /// "1,5" — one and a half, written the European way — used to become fifteen with no error.
    /// </summary>
    [DataTestMethod]
    [DataRow("1,5")]
    [DataRow("12,34")]
    [DataRow("1,2345")]
    [DataRow("1.5,3")]
    [DataRow(",123")]
    public void A_comma_that_is_not_a_thousands_separator_is_not_a_number(string text) =>
        AssertCannotCoerce(text, ValueType.Number);

    [DataTestMethod]
    [DataRow("1,234", "1234")]
    [DataRow("1,234,567.89", "1234567.89")]
    [DataRow("-12,345", "-12345")]
    [DataRow("(100)", "-100")]
    [DataRow("1.5", "1.5")]
    [DataRow(" 42 ", "42")]
    public void Ordinary_numbers_still_convert(string text, string expected) =>
        Assert.AreEqual(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            Coerce(text, ValueType.Number));

    private static object? Coerce(object? value, ValueType? type)
    {
        Assert.IsTrue(Values.TryCoerce(value, type, out var result), $"expected '{value}' to convert to {type}");
        return result;
    }

    private static void AssertCannotCoerce(object? value, ValueType type)
    {
        Assert.IsFalse(Values.TryCoerce(value, type, out var result),
            $"expected '{value}' NOT to convert to {type}, got '{result}'");
        Assert.IsNull(result, "a failed conversion must not leave a value behind");
    }

    // ── null ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A missing source field is an absent value, not a failure — optional fields are ordinary, and
    /// the writer decides how to represent absence.
    /// </summary>
    [TestMethod]
    public void Null_ConvertsToNull_ForEveryType()
    {
        Assert.IsNull(Coerce(null, ValueType.String));
        Assert.IsNull(Coerce(null, ValueType.Number));
        Assert.IsNull(Coerce(null, ValueType.Boolean));
        Assert.IsNull(Coerce(null, null));
    }

    [TestMethod]
    public void NoType_LeavesTheValueAlone()
    {
        Assert.AreEqual("42", Coerce("42", null));
        Assert.AreEqual(42m, Coerce(42m, null));
        Assert.AreEqual(true, Coerce(true, null));
    }

    // ── to string ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void ToString_FromString_IsUnchanged() => Assert.AreEqual("Ali", Coerce("Ali", ValueType.String));

    [TestMethod]
    public void ToString_FromBoolean_IsLowercase()
    {
        Assert.AreEqual("true", Coerce(true, ValueType.String));
        Assert.AreEqual("false", Coerce(false, ValueType.String));
    }

    /// <summary>
    /// A whole number must not pick up a decimal point on the way to text: an order number of 42
    /// becoming "42.0" is a corrupted reference, not a formatting preference.
    /// </summary>
    [TestMethod]
    public void ToString_FromNumber_KeepsTheShortestRoundTrip()
    {
        Assert.AreEqual("42", Coerce(42m, ValueType.String));
        Assert.AreEqual("9.5", Coerce(9.5m, ValueType.String));
        Assert.AreEqual("-0.25", Coerce(-0.25m, ValueType.String));
    }

    // ── to number ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void ToNumber_FromNumber_IsUnchanged() => Assert.AreEqual(9.5m, Coerce(9.5m, ValueType.Number));

    [TestMethod]
    public void ToNumber_FromNumericString_Parses()
    {
        Assert.AreEqual(42m, Coerce("42", ValueType.Number));
        Assert.AreEqual(9.5m, Coerce("9.5", ValueType.Number));
        Assert.AreEqual(-3m, Coerce("-3", ValueType.Number));
    }

    [TestMethod]
    public void ToNumber_FromBoolean_IsOneOrZero()
    {
        Assert.AreEqual(1m, Coerce(true, ValueType.Number));
        Assert.AreEqual(0m, Coerce(false, ValueType.Number));
    }

    /// <summary>
    /// The case that must fail loudly rather than produce null: the exchange names the rule instead
    /// of a partner receiving a document with a hole in it.
    /// </summary>
    [TestMethod]
    public void ToNumber_FromNonNumericString_Fails()
    {
        AssertCannotCoerce("abc", ValueType.Number);
        AssertCannotCoerce("", ValueType.Number);
        AssertCannotCoerce("12 apples", ValueType.Number);
    }

    // ── to boolean ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void ToBoolean_FromBoolean_IsUnchanged()
    {
        Assert.AreEqual(true, Coerce(true, ValueType.Boolean));
        Assert.AreEqual(false, Coerce(false, ValueType.Boolean));
    }

    /// <summary>
    /// XML and CSV carry no types, and <c>xs:boolean</c> itself permits both spellings, so these are
    /// what actually arrives in documents.
    /// </summary>
    [TestMethod]
    public void ToBoolean_FromKnownSpellings_Parses()
    {
        foreach (var text in new[] { "true", "TRUE", " true ", "1", "yes", "Y" })
            Assert.AreEqual(true, Coerce(text, ValueType.Boolean), $"'{text}' should be true");

        foreach (var text in new[] { "false", "FALSE", "0", "no", "N" })
            Assert.AreEqual(false, Coerce(text, ValueType.Boolean), $"'{text}' should be false");
    }

    /// <summary>
    /// Only 0 and 1 have an unambiguous truth value. Treating any non-zero number as true would
    /// silently turn a quantity of 7 into <c>true</c>.
    /// </summary>
    [TestMethod]
    public void ToBoolean_FromNumber_OnlyZeroAndOne()
    {
        Assert.AreEqual(false, Coerce(0m, ValueType.Boolean));
        Assert.AreEqual(true, Coerce(1m, ValueType.Boolean));
        AssertCannotCoerce(7m, ValueType.Boolean);
        AssertCannotCoerce(-1m, ValueType.Boolean);
    }

    [TestMethod]
    public void ToBoolean_FromArbitraryString_Fails()
    {
        AssertCannotCoerce("maybe", ValueType.Boolean);
        AssertCannotCoerce("", ValueType.Boolean);
    }
}

/// <summary>Reading a path out of a document, and placing a value into one.</summary>
[TestClass]
public class ValuesPathTests
{
    /// <summary>Builds <c>{ order: { customer: "Ali", line: [ { sku: "A1" } ] } }</c>.</summary>
    private static ObjectNode Document()
    {
        var line = ValueNode.Object();
        line.Set("sku", ValueNode.Value("A1"));

        var list = ValueNode.List();
        list.Add(line);

        var order = ValueNode.Object();
        order.Set("customer", ValueNode.Value("Ali"));
        order.Set("net", ValueNode.Value(100m));
        order.Set("line", list);

        var root = ValueNode.Object();
        root.Set("order", order);
        return root;
    }

    [TestMethod]
    public void Resolve_ReadsANestedScalar() =>
        Assert.AreEqual("Ali", Values.ResolveScalar(Document(), "order.customer"));

    [TestMethod]
    public void Resolve_ReadsAList() =>
        Assert.IsInstanceOfType<ListNode>(Values.Resolve(Document(), "order.line"));

    [TestMethod]
    public void Resolve_EmptyPath_IsTheRoot() =>
        Assert.IsInstanceOfType<ObjectNode>(Values.Resolve(Document(), ""));

    /// <summary>A path that does not exist is an absent value, not a failure.</summary>
    [TestMethod]
    public void Resolve_MissingPath_IsNull()
    {
        Assert.IsNull(Values.Resolve(Document(), "order.missing"));
        Assert.IsNull(Values.Resolve(Document(), "nothing.here.at.all"));
    }

    /// <summary>Walking through a scalar is a missing path, not an error.</summary>
    [TestMethod]
    public void Resolve_ThroughAScalar_IsNull() =>
        Assert.IsNull(Values.Resolve(Document(), "order.customer.somethingElse"));

    /// <summary>
    /// A path does not step into a list. The old mapper made an array behave as its own first
    /// element, so <c>order.line.sku</c> silently meant "the first line's sku" with no way to say
    /// which was intended. A path into a list belongs to a loop.
    /// </summary>
    [TestMethod]
    public void Resolve_DoesNotStepIntoAList() =>
        Assert.IsNull(Values.Resolve(Document(), "order.line.sku"));

    [TestMethod]
    public void ResolveScalar_OnAListOrObject_IsNull()
    {
        Assert.IsNull(Values.ResolveScalar(Document(), "order.line"));
        Assert.IsNull(Values.ResolveScalar(Document(), "order"));
    }

    // ── placing ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void PlaceAt_NestsThroughSegments()
    {
        var root = ValueNode.Object();
        Values.PlaceAt(root, ["a", "b", "c"], ValueNode.Value(1m));

        Assert.AreEqual(1m, Values.ResolveScalar(root, "a.b.c"));
    }

    /// <summary>
    /// A segment containing a dot stays one key. The old mapper split a dotted target string, so a
    /// key of <c>file.txt</c> became a nested <c>{ file: { txt: … } }</c> and the literal key was
    /// unreachable.
    /// </summary>
    [TestMethod]
    public void PlaceAt_KeepsADotInsideASegment()
    {
        var root = ValueNode.Object();
        Values.PlaceAt(root, ["file.txt"], ValueNode.Value("data"));

        Assert.AreEqual(1, root.Keys.Count);
        Assert.AreEqual("file.txt", root.Keys[0]);
        Assert.IsNull(root["file"]);
    }

    [TestMethod]
    public void PlaceAt_KeepsInsertionOrder()
    {
        var root = ValueNode.Object();
        Values.PlaceAt(root, ["z"], ValueNode.Value(1m));
        Values.PlaceAt(root, ["a"], ValueNode.Value(2m));
        Values.PlaceAt(root, ["m"], ValueNode.Value(3m));

        CollectionAssert.AreEqual(new[] { "z", "a", "m" }, root.Keys.ToArray());
    }

    [TestMethod]
    public void PlaceAt_ReplacingAValue_KeepsItsPosition()
    {
        var root = ValueNode.Object();
        Values.PlaceAt(root, ["first"], ValueNode.Value(1m));
        Values.PlaceAt(root, ["second"], ValueNode.Value(2m));
        Values.PlaceAt(root, ["first"], ValueNode.Value(99m));

        CollectionAssert.AreEqual(new[] { "first", "second" }, root.Keys.ToArray());
        Assert.AreEqual(99m, Values.ResolveScalar(root, "first"));
    }

    /// <summary>
    /// Two rules targeting <c>a</c> and <c>a.b</c> conflict. Last writer wins rather than the deeper
    /// rule being silently dropped; validation is what should surface the conflict.
    /// </summary>
    [TestMethod]
    public void PlaceAt_NestingUnderAScalar_ReplacesIt()
    {
        var root = ValueNode.Object();
        Values.PlaceAt(root, ["a"], ValueNode.Value("scalar"));
        Values.PlaceAt(root, ["a", "b"], ValueNode.Value(1m));

        Assert.AreEqual(1m, Values.ResolveScalar(root, "a.b"));
    }

    [TestMethod]
    public void PlaceAt_NoTarget_DoesNothing()
    {
        var root = ValueNode.Object();
        Values.PlaceAt(root, [], ValueNode.Value(1m));

        Assert.AreEqual(0, root.Keys.Count);
    }
}
