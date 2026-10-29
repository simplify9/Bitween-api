using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// <c>NativeMapper</c> through the real pipeline: rules loaded from the subscription, context
/// supplied by <c>XchangeService</c>, output written to the exchange's Output file.
/// </summary>
/// <remarks>
/// The unit tests cover the engine directly. These cover the wiring around it - that the rules
/// survive the round trip through the database, that the context arrives, and that a payload which
/// is not JSON reaches the mapper at all, which was impossible before the enrichment guard.
/// </remarks>
[Collection("Bitween")]
public class NativeMapperExchangeTests
{
    private readonly BitweenFixture _fixture;

    public NativeMapperExchangeTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record MapperRun(string? Output, string? ContentType, string? Exception);

    private async Task<MapperRun> RunOnce(
        string testName,
        object rules,
        string payload,
        Dictionary<string, string>? partnerProperties = null,
        (string SetId, Dictionary<string, string> Values)? globalSet = null)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xchangeService = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var cache = _fixture.App.Services.GetRequiredService<IInfolinkCache>();

        var partner = new Partner($"{testName} Partner")
        {
            AdapterProperties = partnerProperties ?? new Dictionary<string, string>(),
        };
        db.Set<Partner>().Add(partner);
        await db.SaveChangesAsync();

        if (globalSet is not null)
        {
            db.Set<GlobalAdapterValuesSet>().Add(new GlobalAdapterValuesSet
            {
                Id = globalSet.Value.SetId,
                Name = globalSet.Value.SetId,
                Values = globalSet.Value.Values,
            });
            await db.SaveChangesAsync();
        }

        var document = new Document(null, $"{testName} Doc", DocumentFormat.Json);
        db.Set<Document>().Add(document);
        await db.SaveChangesAsync();

        var subscription = new Subscription($"{testName} Sub", document.Id, SubscriptionType.Internal, partner.Id);
        subscription.Inactive = false;
        subscription.MapperId = nameof(SW.Bitween.NativeAdapters.Mapper.NativeMapper);
        subscription.SetDictionaries(
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["MappingRules"] = JsonConvert.SerializeObject(rules) },
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
        db.Set<Subscription>().Add(subscription);
        await db.SaveChangesAsync();

        cache.Revoke();

        var xchange = await xchangeService.CreateXchange(subscription, new XchangeFile(payload));
        await db.SaveChangesAsync();

        await xchangeService.Process("NativeMapperTest", $"{{\"Id\":\"{xchange.Id}\"}}");

        var result = await db.FindAsync<XchangeResult>(xchange.Id);
        Assert.NotNull(result);

        return result!.Success
            ? new MapperRun(await xchangeService.GetFile(xchange.Id, XchangeFileType.Output),
                result.OutputContentType, null)
            : new MapperRun(null, null, result.Exception);
    }

    [Fact]
    public async Task Maps_a_json_document_using_rules_loaded_from_the_subscription()
    {
        var run = await RunOnce("NM Basic", new
        {
            version = 1,
            sourceFormat = "json",
            targetFormat = "json",
            fields = new object[]
            {
                new { target = new[] { "customerName" }, from = new { kind = "Path", path = "order.customer" } },
                new { target = new[] { "channel" }, from = new { kind = "Fixed", value = "WEB" } },
            },
        }, """{ "order": { "customer": "Ali" } }""");

        Assert.Null(run.Exception);
        var output = JObject.Parse(run.Output!);
        Assert.Equal("Ali", output["customerName"]?.ToString());
        Assert.Equal("WEB", output["channel"]?.ToString());
    }

    /// <summary>
    /// The format's content type reaches the exchange result. The old mapper never set it and the
    /// gateway falls back to <c>application/json</c>, so an XML document would be served as JSON.
    /// </summary>
    [Fact]
    public async Task Reports_the_content_type_of_the_format_it_produced()
    {
        var run = await RunOnce("NM ContentType", new
        {
            version = 1,
            fields = new object[] { new { target = new[] { "a" }, from = new { kind = "Fixed", value = 1 } } },
        }, "{}");

        Assert.Null(run.Exception);
        Assert.Equal("application/json", run.ContentType);
    }

    /// <summary>
    /// Partner and global values arrive as context rather than being read out of the payload - and
    /// the payload here has no room for them, being a root array.
    /// </summary>
    [Fact]
    public async Task Partner_and_global_values_arrive_as_context()
    {
        var setId = $"nm-globals-{Guid.NewGuid():N}";

        var run = await RunOnce("NM Context", new
        {
            version = 1,
            fields = new object[]
            {
                new { target = new[] { "region" }, from = new { kind = "Partner", key = "region-code" } },
                new { target = new[] { "channel" }, from = new { kind = "Global", setId, key = "channel" } },
            },
        },
        """[ { "orderId": "A1" } ]""",
        partnerProperties: new Dictionary<string, string> { ["region-code"] = "JO" },
        globalSet: (setId, new Dictionary<string, string> { ["channel"] = "WEB" }));

        Assert.Null(run.Exception);
        var output = JObject.Parse(run.Output!);

        // Both of these are null under NativeJSONMapper for a root-array payload - the enrichment
        // only touches a JSON object, so the same mapping fails there. See MapperEnrichmentBaselineTests.
        Assert.Equal("JO", output["region"]?.ToString());
        Assert.Equal("WEB", output["channel"]?.ToString());
    }

    /// <summary>
    /// A hyphenated partner key resolves. Under the old mapper this silently produced null inside a
    /// fixed array item, because the key was spliced into a Scriban expression where the hyphen
    /// parsed as subtraction.
    /// </summary>
    [Fact]
    public async Task Partner_key_with_a_hyphen_resolves()
    {
        var run = await RunOnce("NM Hyphen", new
        {
            version = 1,
            fields = new object[]
            {
                new { target = new[] { "key" }, from = new { kind = "Partner", key = "api-key" } },
            },
        }, "{}", partnerProperties: new Dictionary<string, string> { ["api-key"] = "abc123" });

        Assert.Null(run.Exception);
        Assert.Equal("abc123", JObject.Parse(run.Output!)["key"]?.ToString());
    }

    /// <summary>
    /// The payload is handed over untouched, so a document carrying its own <c>__partner__</c> field
    /// keeps it. The enrichment path overwrites one.
    /// </summary>
    [Fact]
    public async Task A_payload_field_called_partner_is_not_overwritten()
    {
        var run = await RunOnce("NM NoClobber", new
        {
            version = 1,
            fields = new object[]
            {
                new { target = new[] { "kept" }, from = new { kind = "Path", path = "__partner__.mine" } },
            },
        },
        """{ "__partner__": { "mine": "from the document" } }""",
        partnerProperties: new Dictionary<string, string> { ["mine"] = "from the partner" });

        Assert.Null(run.Exception);
        Assert.Equal("from the document", JObject.Parse(run.Output!)["kept"]?.ToString());
    }

    /// <summary>
    /// The whole point of the enrichment guard: before it, <c>JToken.Parse</c> in
    /// <c>RunMapper</c> threw on this payload before any mapper ran.
    /// </summary>
    /// <remarks>
    /// It now maps as well as arriving, which makes the same guard easier to check: a payload the
    /// pipeline had mangled on the way past could not produce the value the document holds.
    /// </remarks>
    [Fact]
    public async Task An_xml_payload_reaches_the_mapper_and_maps()
    {
        var run = await RunOnce("NM XmlPayload", new
        {
            version = 1,
            sourceFormat = "xml",
            fields = new object[]
            {
                new { target = new[] { "a" }, from = new { kind = "Fixed", value = 1 } },
                new { target = new[] { "id" }, from = new { kind = "Path", path = "order.id" } },
            },
        }, "<order><id>5</id></order>");

        Assert.Null(run.Exception);
        Assert.Equal("5", JObject.Parse(run.Output!)["id"]?.ToString());
        Assert.Equal("1", JObject.Parse(run.Output!)["a"]?.ToString());
    }

    [Fact]
    public async Task Lists_and_filters_survive_the_round_trip_through_the_database()
    {
        var run = await RunOnce("NM List", new
        {
            version = 1,
            lists = new object[]
            {
                new
                {
                    over = "order.line",
                    @as = "line",
                    target = new[] { "lines" },
                    where = new { field = "qty", @operator = "GreaterThan", value = 0 },
                    fields = new object[]
                    {
                        new { target = new[] { "sku" }, from = new { kind = "Path", path = "sku" } },
                    },
                },
            },
        },
        """
        { "order": { "line": [ { "sku": "A1", "qty": 2 }, { "sku": "B7", "qty": 0 }, { "sku": "C2", "qty": 5 } ] } }
        """);

        Assert.Null(run.Exception);
        var lines = (JArray)JObject.Parse(run.Output!)["lines"]!;
        Assert.Equal(2, lines.Count);
        Assert.Equal("A1", lines[0]["sku"]?.ToString());
        Assert.Equal("C2", lines[1]["sku"]?.ToString());
    }

    /// <summary>
    /// A written entry and the walked ones in one list, through the database and the real
    /// pipeline. The previous mapper had these as two separate features holding literal JSON;
    /// here they are entries built from ordinary rules, so a written one can still read the
    /// document.
    /// </summary>
    [Fact]
    public async Task A_written_entry_comes_before_the_walked_ones()
    {
        var run = await RunOnce("NM Written", new
        {
            version = 1,
            lists = new object[]
            {
                new
                {
                    over = "order.line",
                    target = new[] { "lines" },
                    @fixed = new object[]
                    {
                        new
                        {
                            fields = new object[]
                            {
                                new { target = new[] { "sku" }, from = new { kind = "Fixed", value = "HEADER" } },
                                new { target = new[] { "who" }, from = new { kind = "Path", path = "order.customer" } },
                            },
                        },
                    },
                    fields = new object[]
                    {
                        new { target = new[] { "sku" }, from = new { kind = "Path", path = "sku" } },
                    },
                },
            },
        },
        """
        { "order": { "customer": "Ali", "line": [ { "sku": "A1" }, { "sku": "B7" } ] } }
        """);

        Assert.Null(run.Exception);
        var lines = (JArray)JObject.Parse(run.Output!)["lines"]!;

        Assert.Equal(3, lines.Count);
        Assert.Equal("HEADER", lines[0]["sku"]?.ToString());
        // A written entry has no entry of its own, so its paths read what the list reads.
        Assert.Equal("Ali", lines[0]["who"]?.ToString());
        Assert.Equal("A1", lines[1]["sku"]?.ToString());
        Assert.Equal("B7", lines[2]["sku"]?.ToString());
    }

    /// <summary>
    /// A list that walks nothing is exactly the entries written into it - one slot per rule,
    /// which is what the previous mapper called a primitive array.
    /// </summary>
    [Fact]
    public async Task A_list_that_walks_nothing_is_its_written_entries()
    {
        var run = await RunOnce("NM Slots", new
        {
            version = 1,
            lists = new object[]
            {
                new
                {
                    target = new[] { "codes" },
                    @fixed = new object[]
                    {
                        new { item = new { from = new { kind = "Path", path = "order.customer" } } },
                        new { item = new { from = new { kind = "Fixed", value = "WEB" } } },
                    },
                },
            },
        },
        """{ "order": { "customer": "Ali", "line": [ { "sku": "A1" } ] } }""");

        Assert.Null(run.Exception);
        var codes = (JArray)JObject.Parse(run.Output!)["codes"]!;

        Assert.Equal(new[] { "Ali", "WEB" }, codes.Select(c => c.ToString()).ToArray());
    }

    /// <summary>Every broken rule is named, so one run is enough to fix them all.</summary>
    [Fact]
    public async Task A_failing_rule_fails_the_exchange_and_names_every_one()
    {
        var run = await RunOnce("NM Failure", new
        {
            version = 1,
            fields = new object[]
            {
                new { target = new[] { "a" }, from = new { kind = "Path", path = "text" }, type = "Number" },
                new { target = new[] { "b" }, from = new { kind = "Path", path = "text" }, type = "Boolean" },
            },
        }, """{ "text": "not a number" }""");

        Assert.Null(run.Output);
        Assert.Contains("2 rules", run.Exception);
        Assert.Contains("a: cannot convert 'not a number' to number", run.Exception);
        Assert.Contains("b: cannot convert 'not a number' to boolean", run.Exception);
    }

    [Fact]
    public async Task Rules_from_a_newer_version_are_refused_rather_than_partly_applied()
    {
        var run = await RunOnce("NM Version", new
        {
            version = 99,
            fields = new object[] { new { target = new[] { "a" }, from = new { kind = "Fixed", value = 1 } } },
        }, "{}");

        Assert.Null(run.Output);
        Assert.Contains("version 99", run.Exception);
    }

    [Fact]
    public async Task A_mapper_with_no_rules_says_so()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xchangeService = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var cache = _fixture.App.Services.GetRequiredService<IInfolinkCache>();

        var partner = new Partner("NM NoRules Partner") { AdapterProperties = new Dictionary<string, string>() };
        db.Set<Partner>().Add(partner);
        await db.SaveChangesAsync();

        var document = new Document(null, "NM NoRules Doc", DocumentFormat.Json);
        db.Set<Document>().Add(document);
        await db.SaveChangesAsync();

        var subscription = new Subscription("NM NoRules Sub", document.Id, SubscriptionType.Internal, partner.Id);
        subscription.Inactive = false;
        subscription.MapperId = nameof(SW.Bitween.NativeAdapters.Mapper.NativeMapper);
        db.Set<Subscription>().Add(subscription);
        await db.SaveChangesAsync();
        cache.Revoke();

        var xchange = await xchangeService.CreateXchange(subscription, new XchangeFile("{}"));
        await db.SaveChangesAsync();
        await xchangeService.Process("NativeMapperTest", $"{{\"Id\":\"{xchange.Id}\"}}");

        var result = await db.FindAsync<XchangeResult>(xchange.Id);
        Assert.NotNull(result);
        Assert.False(result!.Success);
        Assert.Contains("no mapping rules configured", result.Exception);
    }
}
