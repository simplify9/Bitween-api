using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Services;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The nightly job that removes old exchanges, the archive it writes first, and the preview the
/// settings page shows before anyone changes either.
/// </summary>
/// <remarks>
/// The collection shares one database, and the job removes every exchange older than its cutoff —
/// so the exchanges here are backdated far past anything another test would create, and the job runs
/// with a cutoff only they fall behind.
/// </remarks>
[Collection("Bitween")]
public class ExchangeRetentionTests(BitweenFixture fixture) : IAsyncLifetime
{
    private const int AncientDays = 5000;
    private const int RetentionDays = 4000;

    private readonly BitweenOptions _options = fixture.App.Services.GetRequiredService<BitweenOptions>();
    private readonly Dictionary<string, string> _settings = new();
    private string _legacyPrefix;

    private static readonly string[] SettingKeys =
    [
        "Bitween.DocumentPrefix", "Bitween.ExchangeRetentionDays"
    ];

    public async Task InitializeAsync()
    {
        _legacyPrefix = _options.LegacyDocumentPrefix;
        await using var scope = fixture.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        foreach (var key in SettingKeys)
            _settings[key] = settings.LiveValue(SettingsCatalog.Find(key));
    }

    public async Task DisposeAsync()
    {
        _options.LegacyDocumentPrefix = _legacyPrefix;
        foreach (var (key, value) in _settings)
            await StoreSetting(key, value);
    }

    [Fact]
    public async Task Old_exchanges_are_archived_and_removed_while_recent_ones_stay()
    {
        var (document, subscription) = await Setup(("OrderNumber", "$.order"), ("Customer", "$.customer"));
        var old = await CreateExchange(subscription, "{\"order\":\"ORD 1001/A\",\"customer\":\"Acme\"}",
            new() { ["Customer"] = "Acme", ["OrderNumber"] = "ORD 1001/A" }, succeeded: true);
        var recent = await CreateExchange(subscription, "{\"order\":\"ORD-1002\"}",
            new() { ["OrderNumber"] = "ORD-1002" }, succeeded: true);
        await AddNotification(old.Id);
        var startedOn = await Backdate(old.Id, AncientDays);

        await RunJob(archive: true);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        Assert.False(await db.Set<Xchange>().AnyAsync(x => x.Id == old.Id));
        Assert.False(await db.Set<XchangeResult>().AnyAsync(r => r.Id == old.Id));
        Assert.False(await db.Set<XchangePromotedProperties>().AnyAsync(p => p.Id == old.Id));
        Assert.False(await db.Set<XchangeNotification>().AnyAsync(n => n.XchangeId == old.Id));
        Assert.True(await db.Set<Xchange>().AnyAsync(x => x.Id == recent.Id));

        // Named by the type's first promoted property, made safe for a key, then the id; filed by
        // subscription and the day it started.
        var archive = scope.ServiceProvider.GetRequiredService<ExchangeArchive>();
        var key = $"{archive.Prefix}/{subscription.Id}/{startedOn:yyyy}/{startedOn:MM}/{startedOn:dd}/ORD-1001-A_{old.Id}.json";
        var archived = JObject.Parse(await Read(key));

        Assert.Equal(old.Id, (string)archived["id"]);
        Assert.Equal("Succeeded", (string)archived["status"]);
        Assert.Equal(subscription.Name, (string)archived["subscription"]!["name"]);
        Assert.Equal(document.Name, (string)archived["informationType"]!["name"]);
        // In the type's order, not the order the values happened to be stored in.
        Assert.Equal(["OrderNumber", "Customer"],
            ((JObject)archived["promotedProperties"]!).Properties().Select(p => p.Name).ToArray());
        Assert.Equal("{\"order\":\"ORD 1001/A\",\"customer\":\"Acme\"}", (string)archived["files"]!["input"]!["content"]);
        Assert.Single((JArray)archived["notifications"]!);
        // Resolved partner values, secrets included, have no place in something read this widely.
        Assert.Null(archived["handlerProperties"]);
    }

    [Fact]
    public async Task A_file_the_bucket_already_deleted_is_archived_as_missing_not_skipped()
    {
        var (_, subscription) = await Setup(("OrderNumber", "$.order"));
        var old = await CreateExchange(subscription, "{\"order\":\"ORD-2001\"}",
            new() { ["OrderNumber"] = "ORD-2001" }, succeeded: true);
        var startedOn = await Backdate(old.Id, AncientDays);

        await using (var scope = fixture.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<XchangeService>();
            await scope.ServiceProvider.GetRequiredService<ICloudFilesService>()
                .DeleteAsync(service.FileKey(old, XchangeFileType.Input));
        }

        await RunJob(archive: true);

        await using var readScope = fixture.CreateScope();
        var prefix = readScope.ServiceProvider.GetRequiredService<ExchangeArchive>().Prefix;
        var archived = JObject.Parse(await Read(
            $"{prefix}/{subscription.Id}/{startedOn:yyyy}/{startedOn:MM}/{startedOn:dd}/ORD-2001_{old.Id}.json"));

        Assert.Null((string)archived["files"]!["input"]!["content"]);
        Assert.NotNull((string)archived["files"]!["input"]!["missing"]);
    }

    [Fact]
    public async Task An_exchange_waiting_for_a_scheduled_retry_is_kept()
    {
        var (_, subscription) = await Setup();
        var waiting = await CreateExchange(subscription, "{}", new(), succeeded: false);
        await Backdate(waiting.Id, AncientDays);
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            db.Add(new DelayedRetry { Id = waiting.Id, On = DateTime.UtcNow.AddHours(1) });
            await db.SaveChangesAsync();
        }

        await RunJob(archive: false);

        await using var readScope = fixture.CreateScope();
        var readDb = readScope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        Assert.True(await readDb.Set<Xchange>().AnyAsync(x => x.Id == waiting.Id));

        // Not left behind for the retry job's tests to find.
        await readDb.Set<DelayedRetry>().Where(d => d.Id == waiting.Id).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task An_exchange_a_newer_retry_still_points_at_stays_until_the_retry_goes()
    {
        var (_, subscription) = await Setup();
        var original = await CreateExchange(subscription, "{}", new(), succeeded: false);
        var retry = await CreateExchange(subscription, "{}", new(), succeeded: true);
        await Backdate(original.Id, AncientDays);
        await using (var scope = fixture.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Xchange>()
                .Where(x => x.Id == retry.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(x => x.RetryFor, original.Id));

        await RunJob(archive: false);
        Assert.True(await Exists(original.Id));

        // Once the retry is old enough too, both go in the same run, the retry first.
        await Backdate(retry.Id, AncientDays);
        await RunJob(archive: false);
        Assert.False(await Exists(retry.Id));
        Assert.False(await Exists(original.Id));
    }

    [Fact]
    public async Task Without_archiving_an_old_exchange_is_removed_and_nothing_is_written()
    {
        var (_, subscription) = await Setup(("OrderNumber", "$.order"));
        var old = await CreateExchange(subscription, "{\"order\":\"ORD-3001\"}",
            new() { ["OrderNumber"] = "ORD-3001" }, succeeded: true);
        await Backdate(old.Id, AncientDays);

        await RunJob(archive: false);

        await using var scope = fixture.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<Xchange>().AnyAsync(x => x.Id == old.Id));
        var prefix = scope.ServiceProvider.GetRequiredService<ExchangeArchive>().Prefix;
        var files = await scope.ServiceProvider.GetRequiredService<ICloudFilesService>()
            .ListAsync($"{prefix}/{subscription.Id}/");
        Assert.Empty(files);
    }

    [Fact]
    public async Task Retention_at_zero_removes_nothing()
    {
        var (_, subscription) = await Setup();
        var old = await CreateExchange(subscription, "{}", new(), succeeded: true);
        await Backdate(old.Id, AncientDays);

        await using (var scope = fixture.CreateScope())
        {
            var job = ActivatorUtilities.CreateInstance<ExchangeRetentionJob>(scope.ServiceProvider,
                new BitweenOptions { ExchangeRetentionDays = 0 });
            await job.Execute();
        }

        await using var readScope = fixture.CreateScope();
        Assert.True(await readScope.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<Xchange>().AnyAsync(x => x.Id == old.Id));
    }

    [Fact]
    public async Task The_preview_says_when_exchanges_outlive_their_files_and_archives_would_lack_them()
    {
        await using var scope = fixture.CreateScope();
        var planner = ActivatorUtilities.CreateInstance<RetentionPlanner>(scope.ServiceProvider,
            ExchangeFilesTests.RetentionOver(new CloudFilesLifecycleRule
                { Id = "temp30", Prefix = "temp30/", Days = 30, Enabled = true }));

        var longer = await planner.Plan(new RetentionProposal
        {
            DocumentPrefix = "temp30/docs", ExchangeRetentionDays = 90, ArchiveExchanges = true
        });
        Assert.Equal(30, longer.Files.Days);
        Assert.Contains(longer.Notices, n => n.Code == "OUTLIVE_FILES" && n.Level == "warning");
        Assert.Contains(longer.Notices, n => n.Code == "ARCHIVE_WITHOUT_FILES");
        Assert.Contains(longer.Notices, n => n.Code == "REMOVES" && n.Message.Contains("archive/docs/"));

        var shorter = await planner.Plan(new RetentionProposal
        {
            DocumentPrefix = "temp30/docs", ExchangeRetentionDays = 20, ArchiveExchanges = true
        });
        Assert.DoesNotContain(shorter.Notices, n => n.Code is "OUTLIVE_FILES" or "ARCHIVE_WITHOUT_FILES");
        // How many are without files is as things stand now, whatever the proposal.
        Assert.NotNull(shorter.Exchanges.WithoutFiles);
        Assert.Equal(longer.Exchanges.WithoutFiles, shorter.Exchanges.WithoutFiles);

        var forever = await planner.Plan(new RetentionProposal { DocumentPrefix = "docs/kept", ExchangeRetentionDays = 0 });
        Assert.Null(forever.Files.Days);
        Assert.Contains(forever.Notices, n => n.Code == "KEEP_FOREVER");
    }

    [Fact]
    public async Task The_preview_counts_what_the_next_run_would_remove()
    {
        var (_, subscription) = await Setup();
        var old = await CreateExchange(subscription, "{}", new(), succeeded: true);
        await Backdate(old.Id, AncientDays);

        await using var scope = fixture.CreateScope();
        var planner = scope.ServiceProvider.GetRequiredService<RetentionPlanner>();

        var plan = await planner.Plan(new RetentionProposal { ExchangeRetentionDays = RetentionDays });
        Assert.True(plan.Exchanges.DueNow >= 1);
        Assert.NotNull(plan.Exchanges.NextRun);

        Assert.Null((await planner.Plan(new RetentionProposal { ExchangeRetentionDays = 0 })).Exchanges.DueNow);
    }

    [Fact]
    public async Task The_document_prefix_is_editable_and_checked()
    {
        await StoreSetting("Bitween.DocumentPrefix", " /temp30/retention-test/ ");
        Assert.Equal("temp30/retention-test", _options.DocumentPrefix);

        var invalid = await Assert.ThrowsAsync<SWValidationException>(() =>
            StoreSetting("Bitween.DocumentPrefix", "temp30/has spaces"));
        Assert.StartsWith("SETTING_INVALID_VALUE", invalid.Message);
        await Assert.ThrowsAsync<SWValidationException>(() => StoreSetting("Bitween.DocumentPrefix", "../outside"));
        await Assert.ThrowsAsync<SWValidationException>(() => StoreSetting("Bitween.DocumentPrefix", "temp30/./docs"));
        Assert.Equal("temp30/retention-test", _options.DocumentPrefix);
    }

    [Fact]
    public async Task The_settings_page_names_aggregations_that_run_less_often_than_exchanges_are_kept()
    {
        var (_, monthly) = await AggregationSetup();
        var (_, daily) = await AggregationSetup();
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        await SetSchedules(db, monthly, new Schedule(Recurrence.Monthly, new TimeSpan(1, 2, 0, 0)));
        await SetSchedules(db, daily, new Schedule(Recurrence.Daily, TimeSpan.FromHours(2)));
        var planner = ActivatorUtilities.CreateInstance<RetentionPlanner>(scope.ServiceProvider);

        var warning = Assert.Single((await planner.Plan(new RetentionProposal { ExchangeRetentionDays = 7 })).Notices,
            n => n.Code == "AGGREGATIONS_MISS_EXCHANGES");
        Assert.Equal("warning", warning.Level);
        Assert.Contains($"{monthly.Name} (up to 31 days between runs)", warning.Message);
        Assert.DoesNotContain(daily.Name, warning.Message);

        Assert.DoesNotContain((await planner.Plan(new RetentionProposal { ExchangeRetentionDays = 0 })).Notices,
            n => n.Code == "AGGREGATIONS_MISS_EXCHANGES");
    }

    [Fact]
    public async Task An_aggregation_is_warned_while_its_schedule_waits_longer_than_exchanges_are_kept()
    {
        await StoreSetting("Bitween.ExchangeRetentionDays", "7");
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.Retention.Aggregation>(scope.ServiceProvider);
        async Task<string> Check(params ScheduleView[] schedules) =>
            ((AggregationRetentionCheck)await handler.Handle(new AggregationRetentionRequest { Schedules = [.. schedules] })).Warning;

        var warning = await Check(new ScheduleView { Recurrence = Recurrence.Monthly, Days = 1, Hours = 2 });
        Assert.Contains("up to 31 days between runs", warning);
        Assert.Contains("removed after 7 days", warning);

        Assert.Null(await Check(new ScheduleView { Recurrence = Recurrence.Weekly, Days = 1, Hours = 8 }));
        Assert.Null(await Check());
    }

    [Fact]
    public async Task The_legacy_prefix_is_remembered_once_and_outlasts_a_change_of_prefix()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();

        await settings.RememberLegacyDocumentPrefix(db);
        var first = await Stored(db, SettingsService.LegacyDocumentPrefixKey);
        Assert.NotNull(first);

        await StoreSetting("Bitween.DocumentPrefix", "temp7/after-the-upgrade");
        await settings.RememberLegacyDocumentPrefix(db);

        Assert.Equal(first, await Stored(db, SettingsService.LegacyDocumentPrefixKey));
    }

    [Fact]
    public async Task An_aggregation_rolls_up_sealed_links_on_the_public_address()
    {
        var (source, aggregation) = await AggregationSetup();
        await using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AggregationJob>()
            .Execute(new AggregationJobParams(aggregation.Id, null));

        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var rollUp = await db.Set<Xchange>().SingleAsync(x => x.SubscriptionId == aggregation.Id);
        var links = JsonConvert.DeserializeObject<string[]>(
            await scope.ServiceProvider.GetRequiredService<XchangeService>().GetFile(rollUp, XchangeFileType.Input));

        // Still a list of links, so handlers written for it are unaffected — just links Bitween
        // serves, since the storage URLs they replace don't open private files.
        var service = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var fileLinks = scope.ServiceProvider.GetRequiredService<FileLinks>();
        var expected = service.FileKey(source, XchangeFileType.Input);
        var link = Assert.Single(links!);
        Assert.StartsWith($"https://bitween.test/{FileLinks.RoutePrefix}/", link);
        var seal = link[$"https://bitween.test/{FileLinks.RoutePrefix}/".Length..].Split('/')[0];
        Assert.True(fileLinks.Opens(seal, expected));
    }

    [Fact]
    public async Task Without_a_public_address_a_roll_up_links_through_the_instances_own_address()
    {
        var (source, aggregation) = await AggregationSetup();
        await using var scope = fixture.CreateScope();
        // A scheduled run has no request to take an address from; the adapters reading the roll-up
        // run inside the instance, so it links through the address the instance listens on.
        var fileLinks = ActivatorUtilities.CreateInstance<FileLinks>(scope.ServiceProvider,
            new BitweenOptions(), new ListeningOn("http://[::]:8080"));
        var xchangeService = ActivatorUtilities.CreateInstance<XchangeService>(scope.ServiceProvider, fileLinks);
        await ActivatorUtilities.CreateInstance<AggregationJob>(scope.ServiceProvider, xchangeService)
            .Execute(new AggregationJobParams(aggregation.Id, null));

        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var rollUp = await db.Set<Xchange>().SingleAsync(x => x.SubscriptionId == aggregation.Id);
        var links = JsonConvert.DeserializeObject<string[]>(await xchangeService.GetFile(rollUp, XchangeFileType.Input));

        var link = Assert.Single(links!);
        Assert.StartsWith($"http://localhost:8080/{FileLinks.RoutePrefix}/", link);
        var seal = link[$"http://localhost:8080/{FileLinks.RoutePrefix}/".Length..].Split('/')[0];
        Assert.True(fileLinks.Opens(seal, xchangeService.FileKey(source, XchangeFileType.Input)));
    }

    [Fact]
    public async Task With_no_address_at_all_a_roll_up_fails_rather_than_list_storage_urls()
    {
        var (_, aggregation) = await AggregationSetup();
        await using var scope = fixture.CreateScope();
        var fileLinks = ActivatorUtilities.CreateInstance<FileLinks>(scope.ServiceProvider,
            new BitweenOptions(), new ListeningOn());
        var xchangeService = ActivatorUtilities.CreateInstance<XchangeService>(scope.ServiceProvider, fileLinks);
        await ActivatorUtilities.CreateInstance<AggregationJob>(scope.ServiceProvider, xchangeService)
            .Execute(new AggregationJobParams(aggregation.Id, null));

        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        Assert.False(await db.Set<Xchange>().AnyAsync(x => x.SubscriptionId == aggregation.Id));
        var attempt = await db.Set<ReceiveAttempt>().AsNoTracking().SingleAsync(a => a.SubscriptionId == aggregation.Id);
        Assert.Equal(ReceiveOutcome.Failed, attempt.Outcome);
        Assert.Contains("no address to build links", attempt.ErrorMessage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Retrying_a_roll_up_made_before_the_upgrade_swaps_its_storage_links_for_served_ones(bool reset)
    {
        var (source, aggregation) = await AggregationSetup();
        await using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var cloudFiles = scope.ServiceProvider.GetRequiredService<ICloudFilesService>();
        var sourceKey = service.FileKey(source, XchangeFileType.Input);
        // What a roll-up held before: the storage URL of each collected file — next to anything else.
        string[] before =
        [
            cloudFiles.GetUrl(sourceKey),
            "https://elsewhere.example/report.csv",
            cloudFiles.GetUrl("adapters/handler.zip")
        ];
        var rollUp = await CreateExchange(aggregation, JsonConvert.SerializeObject(before), new(), succeeded: false);

        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var file = await service.ReadInputFile(rollUp);
        // Retried with its properties reset to the subscription's, or with the ones it had.
        if (reset)
            await service.CreateXchange(await db.Set<Subscription>().SingleAsync(s => s.Id == aggregation.Id), rollUp,
                file, manualRetry: true);
        else
            await service.CreateXchange(rollUp, file, null, manualRetry: true);
        await db.SaveChangesAsync();

        var retry = await db.Set<Xchange>().SingleAsync(x => x.RetryFor == rollUp.Id);
        var after = JsonConvert.DeserializeObject<string[]>(await service.GetFile(retry, XchangeFileType.Input));
        Assert.Equal(scope.ServiceProvider.GetRequiredService<FileLinks>().LinkTo(sourceKey, forAdapter: true), after![0]);
        Assert.StartsWith($"https://bitween.test/{FileLinks.RoutePrefix}/", after[0]);
        Assert.Equal(before[1], after[1]);
        // Not an exchange file, so no link is made for it.
        Assert.Equal(before[2], after[2]);
    }

    [Fact]
    public async Task The_settings_page_warns_when_files_open_without_credentials()
    {
        await using var scope = fixture.CreateScope();
        var open = new FixedStorageAccess(scope.ServiceProvider, _options, true);
        var planner = ActivatorUtilities.CreateInstance<RetentionPlanner>(scope.ServiceProvider, open);

        var warning = Assert.Single((await planner.Plan()).Notices, n => n.Code == "FILES_PUBLIC");
        Assert.Equal("warning", warning.Level);
        Assert.Contains("make it private in the storage provider's console", warning.Message);

        open.CouldNotMakePrivate = "Azure answered 403 AuthorizationPermissionMismatch";
        Assert.Contains("couldn't (Azure answered 403 AuthorizationPermissionMismatch)",
            Assert.Single((await planner.Plan()).Notices, n => n.Code == "FILES_PUBLIC").Message);

        foreach (var verdict in new bool?[] { false, null })
        {
            var quiet = ActivatorUtilities.CreateInstance<RetentionPlanner>(scope.ServiceProvider,
                new FixedStorageAccess(scope.ServiceProvider, _options, verdict));
            Assert.DoesNotContain((await quiet.Plan()).Notices, n => n.Code == "FILES_PUBLIC");
        }
    }

    private sealed class FixedStorageAccess(IServiceProvider serviceProvider, BitweenOptions options, bool? open)
        : StorageAccess(serviceProvider, options, NullLogger<StorageAccess>.Instance)
    {
        public override Task<bool?> IsOpenToAnyoneAsync(bool refresh = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(open);
    }

    // ———————————————————————————————————————————————————————————————— helpers

    private sealed class ListeningOn : IServer
    {
        public ListeningOn(params string[] addresses)
        {
            var feature = new ServerAddressesFeature();
            foreach (var address in addresses) feature.Addresses.Add(address);
            Features.Set<IServerAddressesFeature>(feature);
        }

        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
            where TContext : notnull => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose() { }
    }

    private async Task<bool> Exists(string xchangeId)
    {
        await using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Xchange>()
            .AnyAsync(x => x.Id == xchangeId);
    }

    private async Task RunJob(bool archive)
    {
        await using var scope = fixture.CreateScope();
        var job = ActivatorUtilities.CreateInstance<ExchangeRetentionJob>(scope.ServiceProvider,
            new BitweenOptions { ExchangeRetentionDays = RetentionDays, ArchiveExchanges = archive });
        await job.Execute();
    }

    private async Task<(Document, Subscription)> Setup(params (string Name, string Path)[] promoted)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var document = new Document(null, Unique("Retention type"), DocumentFormat.Json);
        document.SetDictionaries(promoted.ToDictionary(p => p.Name, p => p.Path));
        db.Add(document);
        await db.SaveChangesAsync();

        var subscription = new Subscription(Unique("Retention sub"), document.Id, SubscriptionType.Internal, Partner.SystemId);
        db.Add(subscription);
        await db.SaveChangesAsync();
        fixture.App.Services.GetRequiredService<IInfolinkCache>().Revoke();
        return (document, subscription);
    }

    private static async Task SetSchedules(BitweenDbContext db, Subscription aggregation, params Schedule[] schedules)
    {
        var tracked = await db.Set<Subscription>().SingleAsync(s => s.Id == aggregation.Id);
        tracked.SetSchedules(schedules);
        await db.SaveChangesAsync();
    }

    private async Task<(Xchange Source, Subscription Aggregation)> AggregationSetup()
    {
        var (_, sourceSubscription) = await Setup();
        sourceSubscription.Inactive = false;
        var source = await CreateExchange(sourceSubscription, "{\"agg\":1}", new(), succeeded: true);

        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var aggregation = new Subscription(Unique("Retention agg"), sourceSubscription.Id, Partner.SystemId)
        {
            Inactive = false,
            AggregationTarget = XchangeFileType.Input
        };
        db.Add(aggregation);
        await db.SaveChangesAsync();
        fixture.App.Services.GetRequiredService<IInfolinkCache>().Revoke();
        return (source, aggregation);
    }

    private async Task<Xchange> CreateExchange(Subscription subscription, string payload,
        Dictionary<string, string> promoted, bool succeeded)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var tracked = await db.Set<Subscription>().SingleAsync(s => s.Id == subscription.Id);
        var xchange = await scope.ServiceProvider.GetRequiredService<XchangeService>()
            .CreateXchange(tracked, new XchangeFile(payload));

        var filtered = new FilterResult();
        foreach (var (name, value) in promoted) filtered.Properties[name] = value;
        db.Add(new XchangePromotedProperties(xchange.Id, filtered));
        db.Add(new XchangeResult(xchange.Id, null, null, exception: succeeded ? null : "failed on purpose"));
        await db.SaveChangesAsync();
        return xchange;
    }

    private async Task AddNotification(string xchangeId)
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        db.Add(new XchangeNotification(xchangeId, null, "Retention test notifier"));
        await db.SaveChangesAsync();
    }

    private async Task<DateTime> Backdate(string xchangeId, int days)
    {
        await using var scope = fixture.CreateScope();
        var startedOn = DateTime.UtcNow.AddDays(-days);
        await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Xchange>()
            .Where(x => x.Id == xchangeId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StartedOn, startedOn));
        return startedOn;
    }

    private async Task<string> Read(string key)
    {
        await using var scope = fixture.CreateScope();
        await using var stream = await scope.ServiceProvider.GetRequiredService<ICloudFilesService>().OpenReadAsync(key);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private async Task StoreSetting(string key, string value)
    {
        await using var scope = fixture.CreateScope();
        scope.Superuser();
        var handler = ActivatorUtilities.CreateInstance<Resources.Settings.Update>(scope.ServiceProvider);
        await handler.Handle(key, new SettingUpdate { Value = value });
    }

    private static async Task<string> Stored(BitweenDbContext db, string key) =>
        (await db.Set<Setting>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == key))?.Value;

    private static string Unique(string label) => $"{label} {Guid.NewGuid():N}"[..(label.Length + 9)];
}
