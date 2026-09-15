using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Resources.MappingPreviews;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The preview endpoint, and that it agrees with what a real exchange produces.
/// </summary>
/// <remarks>
/// The last test here is the one that matters. The old mapper's preview built its own template and
/// assembled its own partner values, and the two drifted — a mapping using a partner property on a
/// root-array payload previewed correctly and failed in production. Both paths now run the same
/// three steps against the same context factory, and this pins that.
/// </remarks>
[Collection("Bitween")]
public class MappingPreviewTests
{
    private readonly BitweenFixture _fixture;

    public MappingPreviewTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    private static object BasicRules => new
    {
        version = 1,
        sourceFormat = "json",
        targetFormat = "json",
        fields = new object[]
        {
            new { target = new[] { "customerName" }, from = new { kind = "Path", path = "order.customer" } },
            new { target = new[] { "channel" }, from = new { kind = "Fixed", value = "WEB" } },
        },
    };

    private async Task<MappingPreviewResponse> PreviewAsync(
        object rules, string document, int? partnerId = null)
    {
        await using var scope = _fixture.CreateScope();
        var handler = new Preview(scope.ServiceProvider.GetRequiredService<MappingContextFactory>());

        return await handler.Handle(new MappingPreviewRequest
        {
            MappingRules = JsonConvert.SerializeObject(rules),
            SourceDocument = document,
            PartnerId = partnerId,
        });
    }

    [Fact]
    public async Task Maps_a_sample_document()
    {
        var response = await PreviewAsync(BasicRules, """{ "order": { "customer": "Ali" } }""");

        Assert.Null(response.Error);
        Assert.Empty(response.RuleErrors);
        Assert.Equal("application/json", response.ContentType);

        var output = JObject.Parse(response.OutputDocument!);
        Assert.Equal("Ali", output["customerName"]?.ToString());
        Assert.Equal("WEB", output["channel"]?.ToString());
    }

    /// <summary>Every broken rule is listed separately, so the editor can mark each row.</summary>
    [Fact]
    public async Task Reports_each_failing_rule_separately()
    {
        var response = await PreviewAsync(new
        {
            version = 1,
            fields = new object[]
            {
                new { target = new[] { "a" }, from = new { kind = "Path", path = "text" }, type = "Number" },
                new { target = new[] { "b" }, from = new { kind = "Path", path = "text" }, type = "Boolean" },
            },
        }, """{ "text": "not a number" }""");

        Assert.Null(response.Error);
        Assert.Null(response.OutputDocument);
        Assert.Equal(2, response.RuleErrors.Count);
        Assert.Contains(response.RuleErrors, e => e.Target == "a" && e.Reason.Contains("to number"));
        Assert.Contains(response.RuleErrors, e => e.Target == "b" && e.Reason.Contains("to boolean"));
    }

    /// <summary>
    /// An unreadable document is a general error rather than a rule error — no rule is at fault, and
    /// the editor should say so above the document instead of marking rows.
    /// </summary>
    [Fact]
    public async Task An_unreadable_document_is_a_general_error()
    {
        var response = await PreviewAsync(BasicRules, "<order><id>5</id></order>");

        Assert.Empty(response.RuleErrors);
        Assert.Null(response.OutputDocument);
        Assert.Contains("not valid JSON", response.Error);
    }

    [Fact]
    public async Task An_empty_document_says_so()
    {
        var response = await PreviewAsync(BasicRules, "");

        Assert.Contains("empty", response.Error);
    }

    [Fact]
    public async Task Unreadable_rules_are_a_general_error()
    {
        await using var scope = _fixture.CreateScope();
        var handler = new Preview(scope.ServiceProvider.GetRequiredService<MappingContextFactory>());

        var response = await handler.Handle(new MappingPreviewRequest
        {
            MappingRules = "{ not json",
            SourceDocument = "{}",
        });

        Assert.Contains("could not be read", response.Error);
    }

    [Fact]
    public async Task An_unsupported_format_names_what_is_supported()
    {
        // Named rather than a format that merely happens to be unsupported today: this test asked
        // for "csv" until delimited text landed, at which point it stopped testing anything and
        // failed on a null error message instead. Asserting the whole supported list is the half
        // that has to be kept honest — the next format to arrive is meant to break this line, and
        // it now says so out loud.
        var response = await PreviewAsync(new { version = 1, sourceFormat = "yaml" }, "{}");

        Assert.Contains("not a source format", response.Error);
        Assert.Equal(
            "'yaml' is not a source format this mapper supports. Supported: csv, json, xml.",
            response.Error);
    }

    [Fact]
    public async Task Rules_from_a_newer_version_are_refused()
    {
        var response = await PreviewAsync(new { version = 99 }, "{}");

        Assert.Contains("version 99", response.Error);
    }

    [Fact]
    public async Task Partner_values_are_available_when_a_partner_is_named()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var partner = new Partner("Preview Partner")
        {
            AdapterProperties = new Dictionary<string, string> { ["region-code"] = "JO" },
        };
        db.Set<Partner>().Add(partner);
        await db.SaveChangesAsync();

        var response = await PreviewAsync(new
        {
            version = 1,
            fields = new object[]
            {
                new { target = new[] { "region" }, from = new { kind = "Partner", key = "region-code" } },
            },
        }, "{}", partner.Id);

        Assert.Null(response.Error);
        Assert.Equal("JO", JObject.Parse(response.OutputDocument!)["region"]?.ToString());
    }

    /// <summary>
    /// The same rules, the same document, the same partner — through the preview endpoint and
    /// through a real exchange. Byte for byte.
    /// </summary>
    /// <remarks>
    /// A root-array payload with a partner property, which is exactly the shape the old mapper
    /// disagreed on: its preview injected partner values into every element of an array and its
    /// pipeline injected none, so this mapping previewed with "JO" and then failed.
    /// </remarks>
    [Fact]
    public async Task Preview_and_a_real_exchange_produce_the_same_document()
    {
        const string document = """[ { "orderId": "A1" } ]""";
        var rules = new
        {
            version = 1,
            fields = new object[]
            {
                new { target = new[] { "region" }, from = new { kind = "Partner", key = "region-code" } },
                new { target = new[] { "channel" }, from = new { kind = "Fixed", value = "WEB" } },
            },
            lists = new object[]
            {
                new
                {
                    over = "",
                    target = new[] { "orders" },
                    fields = new object[]
                    {
                        new { target = new[] { "id" }, from = new { kind = "Path", path = "orderId" } },
                    },
                },
            },
        };

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xchangeService = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var cache = _fixture.App.Services.GetRequiredService<IInfolinkCache>();

        var partner = new Partner("Agreement Partner")
        {
            AdapterProperties = new Dictionary<string, string> { ["region-code"] = "JO" },
        };
        db.Set<Partner>().Add(partner);
        await db.SaveChangesAsync();

        var doc = new Document(null, "Agreement Doc", DocumentFormat.Json);
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var subscription = new Subscription("Agreement Sub", doc.Id, SubscriptionType.Internal, partner.Id);
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

        var xchange = await xchangeService.CreateXchange(subscription, new XchangeFile(document));
        await db.SaveChangesAsync();
        await xchangeService.Process("PreviewAgreement", $"{{\"Id\":\"{xchange.Id}\"}}");

        var result = await db.FindAsync<XchangeResult>(xchange.Id);
        Assert.NotNull(result);
        Assert.True(result!.Success, result.Exception);

        var fromExchange = await xchangeService.GetFile(xchange.Id, XchangeFileType.Output);
        var fromPreview = await PreviewAsync(rules, document, partner.Id);

        Assert.Null(fromPreview.Error);
        Assert.Equal(fromExchange, fromPreview.OutputDocument);
        Assert.Equal(result.OutputContentType, fromPreview.ContentType);
    }
}
