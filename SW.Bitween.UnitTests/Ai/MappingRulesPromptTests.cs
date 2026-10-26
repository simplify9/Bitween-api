using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters.Mapper;
using SW.Bitween.NativeAdapters.Mapper.Formats;
using SW.Bitween.Services.Ai;

namespace SW.Bitween.UnitTests.Ai;

/// <summary>
/// A model's answer becomes rules only if the mapper would read it the same way: unknown properties
/// are refused rather than dropped, and the editor's formats always win.
/// </summary>
[TestClass]
public class MappingRulesPromptTests
{
    static readonly MappingRules Editor = new()
    {
        SourceFormat = "csv",
        TargetFormat = "json",
        SourceCsv = new CsvOptions { Delimiter = ";", HasHeader = true },
    };

    [TestMethod]
    public void An_answer_in_a_code_fence_is_read()
    {
        var rules = MappingRulesPrompt.Parse("""
            Here are the rules:
            ```json
            { "version": 1, "root": { "over": "", "fields": [ { "target": ["sku"], "from": { "kind": "path", "path": "SKU" } } ] } }
            ```
            """, Editor);

        Assert.AreEqual("SKU", rules.Root!.Fields[0].From.Path);
        Assert.AreEqual(ValueSourceKind.Path, rules.Root.Fields[0].From.Kind);
    }

    [TestMethod]
    public void The_editors_formats_replace_whatever_the_model_wrote()
    {
        var rules = MappingRulesPrompt.Parse("""
            { "version": 1, "sourceFormat": "json", "targetFormat": "xml", "sourceCsv": { "delimiter": "," },
              "fields": [ { "target": ["a"], "from": { "kind": "fixed", "value": 1 } } ] }
            """, Editor);

        Assert.AreEqual("csv", rules.SourceFormat);
        Assert.AreEqual("json", rules.TargetFormat);
        Assert.AreEqual(";", rules.SourceCsv!.Delimiter);
    }

    /// <summary>A property the mapper doesn't know is a rule the model meant and the mapper would silently skip.</summary>
    [TestMethod]
    public void An_unknown_property_is_refused_with_its_name()
    {
        var refused = Assert.ThrowsException<FormatException>(() => MappingRulesPrompt.Parse("""
            { "version": 1, "fields": [ { "target": ["a"], "from": { "kind": "path", "path": "x" }, "condition": "x > 1" } ] }
            """, Editor));

        StringAssert.Contains(refused.Message, "condition");
    }

    [TestMethod]
    public void A_transforms_arguments_are_not_unknown_properties()
    {
        var rules = MappingRulesPrompt.Parse("""
            { "version": 1, "fields": [ { "target": ["a"], "from": { "kind": "path", "path": "x" },
              "transform": { "fn": "substring", "start": 0, "length": 3 } } ] }
            """, Editor);

        Assert.AreEqual(3, (int)rules.Fields[0].Transform!.Args["length"]);
    }

    [TestMethod]
    public void An_answer_that_is_not_rules_says_why()
    {
        StringAssert.Contains(Assert.ThrowsException<FormatException>(() => MappingRulesPrompt.Parse("I can't help with that.", Editor)).Message, "no JSON");
        StringAssert.Contains(Assert.ThrowsException<FormatException>(() => MappingRulesPrompt.Parse("""{ "version": 2 }""", Editor)).Message, "version");
        StringAssert.Contains(Assert.ThrowsException<FormatException>(() => MappingRulesPrompt.Parse("""
            { "version": 1, "lists": [ { "over": "x", "target": ["a"], "fields": null } ] }
            """, Editor)).Message, "can't be null");
        StringAssert.Contains(Assert.ThrowsException<FormatException>(() => MappingRulesPrompt.Parse("""
            { "version": 1, "root": { "over": "" }, "fields": [ { "target": ["a"], "from": { "kind": "fixed" } } ] }
            """, Editor)).Message, "root");
    }

    /// <summary>The editor stores what this writes, so it has to read back as the same rules.</summary>
    [TestMethod]
    public void Serialized_rules_are_the_editors_wire_format()
    {
        var rules = MappingRulesPrompt.Parse("""
            { "version": 1, "fields": [ { "target": ["status"], "from": { "kind": "rootPath", "path": "s" },
              "lookup": { "table": { "N": "New" } }, "type": "string" } ],
              "lists": [ { "over": "", "target": ["rows"], "fields": [] }, { "target": ["fixedOnly"], "fixed": [] } ] }
            """, new MappingRules());

        var json = MappingRulesPrompt.Serialize(rules);

        StringAssert.Contains(json, "\"kind\":\"rootPath\"");
        StringAssert.Contains(json, "\"type\":\"string\"");
        // A lookup's keys are data, and keep their case.
        StringAssert.Contains(json, "\"N\":\"New\"");
        // An empty "over" walks the document; an absent one walks nothing. Both survive.
        StringAssert.Contains(json, "\"over\":\"\"");
        Assert.AreEqual(1, json.Split("\"over\"").Length - 1);
    }

    [TestMethod]
    public void A_refinement_message_carries_the_rules_and_what_was_wrong()
    {
        var message = MappingRulesPrompt.Message(Editor, "SKU;QTY\nA1;2", "[{\"sku\":\"A1\"}]",
            previous: (new MappingRules(), ["sku: missing; the output sample has the text \"A1\""]));

        StringAssert.Contains(message, "\"delimiter\":\";\"");
        StringAssert.Contains(message, "<source_sample>\nSKU;QTY\nA1;2\n</source_sample>".Replace("\n", Environment.NewLine));
        StringAssert.Contains(message, "- sku: missing");
        Assert.IsFalse(message.Contains("<instructions>"), "no instructions were given");
    }

    [TestMethod]
    public void The_message_carries_instructions_and_names_but_never_values()
    {
        var context = new MappingContext
        {
            Partner = new Dictionary<string, string> { ["SenderId"] = "ACME-7", ["ApiPassword"] = "__private__" },
            Globals = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["regions"] = new Dictionary<string, string> { ["north"] = "N-1", ["default"] = "D-0" },
                ["keys"] = new Dictionary<string, string> { ["token"] = "__private__" },
            },
        };

        var message = MappingRulesPrompt.Message(Editor, "a;b", "[]", MappingRulesPrompt.NamesOf(context),
            "C means Cancelled.");

        StringAssert.Contains(message, "- partner properties: SenderId");
        StringAssert.Contains(message, "- global value set \"regions\", keys: default, north");
        StringAssert.Contains(message, "<instructions>" + Environment.NewLine + "C means Cancelled.");
        foreach (var leaked in new[] { "ACME-7", "N-1", "D-0", "ApiPassword", "token", "\"keys\"" })
            Assert.IsFalse(message.Contains(leaked), $"'{leaked}' reached the message");
    }
}
