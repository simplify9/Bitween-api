using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// Pins down what <c>XchangeService.RunMapper</c> does to a payload before handing it to
/// <c>NativeJSONMapper</c>, so that adding a new mapper alongside it cannot change any of it.
/// </summary>
/// <remarks>
/// <para>
/// Partner and global values reach a Scriban template by being written <em>into the payload</em> as
/// <c>__partner__</c> and <c>__globals__</c>. Every template in production reads them from there, so
/// the enrichment is load-bearing behaviour rather than an implementation detail - which is why it
/// gets pinned before the guard that skips it for context-aware mappers is written, not after.
/// </para>
/// <para>
/// Two of these tests assert behaviour that is arguably wrong: enrichment is skipped for a root-array
/// payload even though the preview endpoint enriches one, and a non-object payload silently maps
/// against no partner at all. They are pinned as-is deliberately. Existing subscriptions are working
/// against exactly this, and "fixing" it here would change what they produce.
/// </para>
/// <para>
/// <c>SW.Bitween.UnitTests/RunMapperEnrichmentTests.cs</c> covers the same ground with its own local
/// copy of the parsing line, so it passes whatever <c>XchangeService</c> does. These go through the
/// real pipeline instead.
/// </para>
/// </remarks>
[Collection("Bitween")]
public class MapperEnrichmentBaselineTests
{
    private readonly BitweenFixture _fixture;

    public MapperEnrichmentBaselineTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Runs one exchange all the way through the mapper step and returns what the mapper wrote.
    /// </summary>
    /// <remarks>
    /// No handler is configured: <c>RunHandler</c> returns immediately on a null <c>HandlerId</c>,
    /// which leaves the mapper's output as the last thing the pipeline produced - and it is already
    /// persisted, because <c>RunMapper</c> stores it as the <see cref="XchangeFileType.Output"/> file.
    /// </remarks>
    private sealed record MapperRun(string? Output, string? Exception);

    private async Task<MapperRun> RunMapperOnce(
        string testName,
        string scribanTemplate,
        string payload,
        Dictionary<string, string>? partnerProperties = null,
        (string SetId, Dictionary<string, string> Values)? globalSet = null)
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xchangeService = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var cache = _fixture.App.Services.GetRequiredService<IInfolinkCache>();

        // A partner always exists, because the Internal-subscription constructor takes a non-null
        // partner id. Enrichment keys off whether it has any properties (`AdapterProperties?.Count
        // > 0`), so an empty dictionary is how a test says "partner present, nothing to inject".
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
        subscription.MapperId = nameof(SW.Bitween.NativeAdapters.NativeJSONMapper);
        subscription.SetDictionaries(
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["ScribanTemplate"] = scribanTemplate },
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>());
        db.Set<Subscription>().Add(subscription);
        await db.SaveChangesAsync();

        // The global-values list and the subscription itself are both read through the cache during
        // CreateXchange, so rows added above are invisible until it is dropped.
        cache.Revoke();

        var xchange = await xchangeService.CreateXchange(subscription, new XchangeFile(payload));
        await db.SaveChangesAsync();

        // Process(name, json) is the only public way in; the message type just has to not be the
        // result queue, and XchangeMessage is internal so the payload is written by hand.
        await xchangeService.Process("BaselineTest", $"{{\"Id\":\"{xchange.Id}\"}}");

        // Process swallows any failure into the XchangeResult rather than rethrowing, and only
        // writes the Output file when the mapper actually produced one - so a mapping that blew up
        // has no file to read, and the reason is on the result.
        // An XchangeResult's own Id is the exchange id it belongs to.
        var result = await db.FindAsync<XchangeResult>(xchange.Id);
        Assert.NotNull(result);

        return result.Success
            ? new MapperRun(await xchangeService.GetFile(xchange.Id, XchangeFileType.Output), null)
            : new MapperRun(null, result.Exception);
    }

    [Fact]
    public async Task Partner_properties_are_injected_into_an_object_payload()
    {
        var run = await RunMapperOnce(
            "Baseline Partner",
            "{ \"region\": {{ __partner__.regionCode | json }} }",
            "{\"orderId\":\"A1\"}",
            partnerProperties: new Dictionary<string, string> { ["regionCode"] = "JO" });

        Assert.Null(run.Exception);
        Assert.Equal("JO", JObject.Parse(run.Output!)["region"]?.ToString());
    }

    [Fact]
    public async Task Global_values_are_injected_into_an_object_payload()
    {
        var setId = $"baseline-globals-{Guid.NewGuid():N}";

        var run = await RunMapperOnce(
            "Baseline Globals",
            $"{{ \"channel\": {{{{ __globals__[\"{setId}\"].channel | json }}}} }}",
            "{\"orderId\":\"A2\"}",
            globalSet: (setId, new Dictionary<string, string> { ["channel"] = "WEB" }));

        Assert.Null(run.Exception);
        Assert.Equal("WEB", JObject.Parse(run.Output!)["channel"]?.ToString());
    }

    [Fact]
    public async Task Partner_and_globals_are_injected_together()
    {
        var setId = $"baseline-both-{Guid.NewGuid():N}";

        var run = await RunMapperOnce(
            "Baseline Both",
            $"{{ \"region\": {{{{ __partner__.regionCode | json }}}}, \"channel\": {{{{ __globals__[\"{setId}\"].channel | json }}}} }}",
            "{\"orderId\":\"A3\"}",
            partnerProperties: new Dictionary<string, string> { ["regionCode"] = "JO" },
            globalSet: (setId, new Dictionary<string, string> { ["channel"] = "WEB" }));

        Assert.Null(run.Exception);
        var result = JObject.Parse(run.Output!);
        Assert.Equal("JO", result["region"]?.ToString());
        Assert.Equal("WEB", result["channel"]?.ToString());
    }

    /// <summary>
    /// A root-array payload is not enriched, because the enrichment only runs when the payload
    /// parses as a <c>JObject</c> - so a template reading <c>__partner__</c> fails the exchange.
    /// </summary>
    /// <remarks>
    /// The preview endpoint <em>does</em> inject into every element of a root array, so this mapping
    /// previews with "JO" in it and then fails in production. Pinned as the current behaviour, not
    /// endorsed: fixing it would change what existing subscriptions do.
    /// </remarks>
    [Fact]
    public async Task Root_array_payload_is_not_enriched_and_the_exchange_fails()
    {
        var run = await RunMapperOnce(
            "Baseline Array",
            "{ \"region\": {{ __partner__.regionCode | json }} }",
            "[{\"orderId\":\"A4\"}]",
            partnerProperties: new Dictionary<string, string> { ["regionCode"] = "JO" });

        Assert.Null(run.Output);
        Assert.NotNull(run.Exception);
    }

    /// <summary>
    /// A payload that is valid JSON but not an object - a receiver handing back a JSON-encoded
    /// string - is not enriched either, and fails the same way.
    /// </summary>
    /// <remarks>
    /// This is the case <c>RunMapperEnrichmentTests</c> was added for. It proves only that the
    /// <em>parse</em> no longer throws; the exchange still fails further down, at the template.
    /// </remarks>
    [Fact]
    public async Task Non_object_payload_is_not_enriched_and_the_exchange_fails()
    {
        var run = await RunMapperOnce(
            "Baseline String",
            "{ \"region\": {{ __partner__.regionCode | json }} }",
            "\"a plain string response from the receiver\"",
            partnerProperties: new Dictionary<string, string> { ["regionCode"] = "JO" });

        Assert.Null(run.Output);
        Assert.NotNull(run.Exception);
    }

    /// <summary>
    /// A payload's own fields still map when the partner has nothing to contribute.
    /// </summary>
    /// <remarks>
    /// Deliberately does not assert that the payload is untouched. Enrichment reads <em>every</em>
    /// global values set in the database, and the fixture's database is shared across the whole
    /// collection - so a set added by any other test would appear in this payload too. What the
    /// added keys are called is pinned by the tests above; this one pins that adding them does not
    /// disturb the fields that were already there.
    /// </remarks>
    [Fact]
    public async Task Payload_own_fields_still_map_when_partner_has_no_properties()
    {
        var run = await RunMapperOnce(
            "Baseline Plain",
            "{ \"id\": {{ orderId | json }} }",
            "{\"orderId\":\"A5\"}");

        Assert.Null(run.Exception);
        Assert.Equal("A5", JObject.Parse(run.Output!)["id"]?.ToString());
    }
}
