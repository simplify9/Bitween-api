using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Cli.Configuration;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using static SW.Bitween.Services.Mirror.MirrorCompare;

namespace SW.Bitween.Services.Mirror;

public sealed partial class MirrorService
{
    /// <summary>Where staging keeps each subscription's test: prod's arrivals, faked, and how they last ran.</summary>
    public const string TestsPrefix = "mirror/tests";

    /// <summary>How each subscription's test last ran, by its name, in one place for the lists to read.</summary>
    const string VerdictsKey = TestsPrefix + "/verdicts.json";

    /// <summary>The verdict of a subscription prod had none of to take, when its arrivals were last taken.</summary>
    public const string NoTraffic = "noTraffic";

    static readonly MirrorTest NoTest = new("idle", null, 0, 0, null, null, null, [], null, [], null, [], 0);

    /// <summary>A subscription's test's place in storage: by a hash of its name, never a path a name could choose.</summary>
    static string TestFolder(string name) => $"{TestsPrefix}/{Folder(name)}";

    // ------------------------------------------------------------------ the subscription beside prod's

    /// <summary>
    /// One of staging's subscriptions beside prod's of the same name: it and each item it needs to run as
    /// on prod (see <see cref="Needs"/>), each the same, faked, different field by field or not on staging,
    /// with the adapters they run; what bringing it from prod would do; and what only a person can do first.
    /// </summary>
    public async Task<MirrorSubscriptionView> SubscriptionAsync(string name)
    {
        using var comparison = await CompareAsync(removeExtras: false, copyKey: null);
        var row = comparison.Parity.Kinds.First(k => k.Section == "subscriptions").Items.FirstOrDefault(i => i.Key == name && i.State != "extra");
        if (row == null) return new MirrorSubscriptionView(name, false, null, comparison.Parity.Prod.Url, [], [], []);

        var bringing = await PlanBringingAsync(comparison, Kinds.Of("subscriptions"), [name]);
        var setup = new Setup(comparison.Parity, comparison.Plan, comparison.Copy);
        var order = Kinds.All.Select(k => k.Section).ToList();
        var used = bringing.Needs.Keys
            .OrderBy(n => n == ("subscriptions", name) ? -1 : order.IndexOf(n.Section)).ThenBy(n => n.Key, StringComparer.Ordinal)
            .Select(n => Item(setup, n.Section, n.Key, Capital(Kinds.Of(n.Section).Noun)))
            .Concat(bringing.Adapters.Select(AdapterLine))
            .ToList();
        return new MirrorSubscriptionView(name, true, row.ProdId, comparison.Parity.Prod.Url, used,
            bringing.Pieces.Where(p => p.State != "kept").ToList(), bringing.NeedsYou);
    }

    /// <summary>An adapter the subscription and what it needs run, prod's version beside staging's.</summary>
    static MirrorDiff AdapterLine(MirrorAdapter a)
    {
        var name = $"Adapter {a.Id}";
        return a.State switch
        {
            "same" => new MirrorDiff("used", name, Same, a.ProdVersion, a.StagingVersion),
            "different" => new MirrorDiff("used", name, Info, a.ProdVersion, a.StagingVersion,
                $"Staging runs version {a.StagingVersion} where prod runs {a.ProdVersion}."),
            _ when a.Source == "builtin" => new MirrorDiff("used", name, Different, "Built in", "Not in this release",
                "This release no longer has it, so what runs it would stop working after the upgrade."),
            _ => new MirrorDiff("used", name, Info, a.ProdVersion ?? "Has it", "Doesn't have it",
                a.Source == "marketplace" && a.ProdVersion != null ? "Bringing it from prod installs it from the marketplace." : "Only a person can add it: see what's left to do."),
        };
    }

    // ------------------------------------------------------------------ its test

    /// <summary>
    /// A subscription's test as the page shows it: each file with a link that opens it, how work for it is
    /// going, and how many exchanges its runs have left on staging.
    /// </summary>
    public async Task<MirrorTest> TestAsync(string name, MirrorWork work)
    {
        var test = await ReadTestAsync(name);
        using var scope = scopes.CreateScope();
        test = test with
        {
            Families = (test.Families ?? []).Select(f => f with { Members = f.Members.Select(Linked).ToList() }).ToList(),
            Runs = (test.Runs ?? []).Select(Linked).ToList(),
            Made = test.Made ?? [],
            Exchanges = await MadeBy(test.Made ?? [], scope.ServiceProvider.GetRequiredService<BitweenDbContext>()).CountAsync(),
            State = "idle", Busy = null, Done = 0, Total = 0, Error = null,
            // The page says itself when prod had none to take.
            Last = (await ReadVerdictsAsync()).GetValueOrDefault(name) is { Verdict: not NoTraffic } last ? last : null,
        };
        if (work.Running is { } running)
            return running.Subject == name
                ? test with { State = running.Kind == MirrorWork.Sampling ? "taking" : "running", Done = running.Done, Total = running.Total }
                : test with { State = work.Waiting.Contains(name) ? "queued" : "idle", Busy = MirrorWork.Describe(running) };
        return (work.LastError(MirrorWork.Sampling, name) ?? work.LastError(MirrorWork.Rehearsing, name)) is { } error
            ? test with { State = "failed", Error = error }
            : test;
    }

    /// <summary>
    /// Takes prod's latest arrivals a subscription takes part in: up to five of its exchanges, mostly ones
    /// that worked, each with what came in and everything it led to, faked as they arrive, the way
    /// <see cref="TakeSamplesAsync"/> takes them for every subscription. Replaces the ones taken before;
    /// what earlier runs made on staging stays until the test is cleared.
    /// </summary>
    public async Task<MirrorTest> TakeTestAsync(string name, Action<int, int> progress, CancellationToken cancellation = default)
    {
        if (!options.Mirror.Target) throw new MirrorRefusal(NotTarget);
        using var prod = Connect();
        await VersionAsync(prod);
        var ctx = new Context(prod);
        var skipped = await ReadAsync(ctx, []);
        if (skipped.TryGetValue("subscriptions", out var why)) throw new MirrorRefusal($"Prod's subscriptions couldn't be read. {why}");
        var subscription = (ctx.Current.GetValueOrDefault("subscriptions") ?? []).FirstOrDefault(s => s.Key == name)
                           ?? throw new MirrorRefusal($"Prod doesn't have a subscription “{name}”, so there's nothing of prod's to test it with.");
        var (_, faker) = CopyOf(ctx);
        using var files = new HttpClient(new ReadOnly(new HttpClientHandler { UseCookies = false })) { Timeout = TimeSpan.FromSeconds(60) };
        var folder = TestFolder(name);
        var taking = new Taking(prod, ctx, files, faker, $"{folder}/files");
        progress?.Invoke(0, 1);
        var taken = await SamplesOfAsync(taking, subscription, cancellation);
        var families = taken.Families.Select(id => taking.Families[id]).ToList();

        var test = (await ReadTestAsync(name)) with
        {
            TakenOn = DateTimeOffset.UtcNow, Reason = families.Count == 0 ? taken.Reason : null, Families = families,
            RanOn = null, Runs = [], SetupError = null,
        };
        await SaveTestAsync(name, test);
        // How the arrivals taken before ran says nothing of these.
        await SaveVerdictAsync(name, families.Count == 0 ? new MirrorTestVerdict(NoTraffic, test.TakenOn.Value, 0, 0, 0, 0) : null);
        var kept = families.SelectMany(f => f.Members).SelectMany(s => new[] { s.Input, s.Output, s.Response }.Concat(s.Rolled ?? []))
            .Where(f => f?.Key != null).Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var file in await cloudFiles.ListAsync($"{folder}/files/"))
            if (!kept.Contains(file.Key))
                await cloudFiles.DeleteAsync(file.Key);
        progress?.Invoke(1, 1);
        return test;
    }

    /// <summary>
    /// Sends each of the subscription's arrivals in on staging the way it came in on prod, and compares
    /// every exchange it led to with its twin on prod, as a rehearsal does. Staging's setup is read as
    /// whoever started it, with <paramref name="selfToken"/>, since nobody's request is around by then.
    /// </summary>
    public async Task<MirrorTest> RunTestAsync(string name, Action<int, int> progress, string selfToken, CancellationToken cancellation = default)
    {
        if (!options.Mirror.Target) throw new MirrorRefusal(NotTarget);
        var test = await ReadTestAsync(name);
        var families = InOrder(test.Families);
        if (families.Count == 0) throw new MirrorRefusal("There are none of prod's arrivals to send in yet: take them first.");
        progress?.Invoke(0, families.Count);

        var (setup, setupError) = await SetupAsync(selfToken);
        var startedOn = DateTimeOffset.UtcNow;
        var made = (test.Made ?? []).ToList();
        test = test with { RanOn = startedOn, Runs = [], SetupError = setupError };
        await RunFamiliesAsync(families, setup, progress, runs =>
        {
            // Saved as it goes: the page shows each arrival once it is compared, and a clear finds what it made.
            made.AddRange(runs.Where(r => r.Correlation != null && made.All(m => m.Correlation != r.Correlation))
                .Select(r => new MirrorTestRun(r.Correlation, startedOn)));
            test = test with { Runs = runs, Made = made.ToList() };
            return SaveTestAsync(name, test);
        }, cancellation);
        await SaveVerdictAsync(name, VerdictOf(test.Runs, startedOn));
        return test;
    }

    /// <summary>
    /// The work that tests several subscriptions, one after another: for each, prod's latest arrivals are
    /// taken first unless some are, then sent in. One prod has none of to take is passed over.
    /// </summary>
    public async Task<IReadOnlyList<MirrorJob>> TestJobsAsync(IEnumerable<string> names, string selfToken)
    {
        var jobs = new List<MirrorJob>();
        foreach (var name in names)
        {
            if ((await ReadTestAsync(name)).Families is not { Count: > 0 })
                jobs.Add(new MirrorJob(MirrorWork.Sampling, name, (m, progress, ct) => m.TakeTestAsync(name, progress, ct)));
            jobs.Add(new MirrorJob(MirrorWork.Rehearsing, name, async (m, progress, ct) =>
            {
                if ((await m.ReadTestAsync(name)).Families is { Count: > 0 })
                    await m.RunTestAsync(name, progress, selfToken, ct);
            }));
        }
        return jobs;
    }

    /// <summary>The worst of how the arrivals ran; one that didn't run is one to check.</summary>
    internal static MirrorTestVerdict VerdictOf(IReadOnlyList<MirrorFamilyRun> runs, DateTimeOffset ranOn)
    {
        int Count(string verdict) => runs.Count(r => r.Verdict == verdict);
        var notRun = runs.Count(r => r.Verdict == null);
        var verdict = Count(Different) > 0 ? Different : Count(Check) > 0 || notRun > 0 ? Check : Same;
        return new MirrorTestVerdict(verdict, ranOn, Count(Same), Count(Check), Count(Different), notRun);
    }

    /// <summary>
    /// Removes what the subscription's test made on staging: every exchange its runs started - each arrival
    /// and everything it led to - with its results, notifications, scheduled retries and files, and each
    /// arrival's results. Prod's arrivals stay, to run again, and so does how the last run went, for the lists.
    /// </summary>
    public async Task<MirrorTest> ClearTestAsync(string name)
    {
        var test = await ReadTestAsync(name);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xchanges = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var rows = await (from x in MadeBy(test.Made ?? [], db)
                          join r in db.Set<XchangeResult>().AsNoTracking() on x.Id equals r.Id into results
                          from r in results.DefaultIfEmpty()
                          select new { x.Id, x.FilesPrefix, x.InputSize, OutputSize = (int?)r.OutputSize, ResponseSize = (int?)r.ResponseSize })
            .ToListAsync();
        foreach (var row in rows)
        foreach (var (size, type) in new[] { ((int?)row.InputSize, XchangeFileType.Input), (row.OutputSize, XchangeFileType.Output), (row.ResponseSize, XchangeFileType.Response) })
            if (xchanges.FileKey(row.Id, row.FilesPrefix, size, type) is { } key)
                try { await cloudFiles.DeleteAsync(key); }
                catch (Exception ex) when (StorageErrors.IsNotFound(ex)) { }

        // Results, promoted properties, aggregation links and deliveries go with an exchange; these don't.
        var ids = rows.Select(r => r.Id).ToList();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await db.Set<XchangeNotification>().Where(n => ids.Contains(n.XchangeId)).ExecuteDeleteAsync();
            await db.Set<DelayedRetry>().Where(d => ids.Contains(d.Id)).ExecuteDeleteAsync();
            await db.Set<Xchange>().Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }
        test = test with { RanOn = null, Runs = [], SetupError = null, Made = [] };
        await SaveTestAsync(name, test);
        return test;
    }

    /// <summary>The exchanges a test's runs made on staging: each run's correlation, from when it started, which the index on StartedOn narrows to.</summary>
    static IQueryable<Xchange> MadeBy(IReadOnlyList<MirrorTestRun> made, BitweenDbContext db)
    {
        if (made.Count == 0) return db.Set<Xchange>().AsNoTracking().Where(_ => false);
        var since = made.Min(m => m.StartedOn).UtcDateTime.AddMinutes(-1);
        var correlations = made.Select(m => m.Correlation).ToList();
        return db.Set<Xchange>().AsNoTracking().Where(x => x.StartedOn >= since && correlations.Contains(x.CorrelationId));
    }

    async Task<MirrorTest> ReadTestAsync(string name)
    {
        try
        {
            await using var stream = await cloudFiles.OpenReadAsync($"{TestFolder(name)}/test.json");
            return await JsonSerializer.DeserializeAsync<MirrorTest>(stream, ManifestJson) ?? NoTest;
        }
        catch (Exception ex) when (StorageErrors.IsNotFound(ex))
        {
            return NoTest;
        }
    }

    Task SaveTestAsync(string name, MirrorTest test) =>
        cloudFiles.WriteTextAsync(JsonSerializer.Serialize(test, ManifestJson),
            new WriteFileSettings { Key = $"{TestFolder(name)}/test.json", ContentType = "application/json" });

    async Task<Dictionary<string, MirrorTestVerdict>> ReadVerdictsAsync()
    {
        try
        {
            await using var stream = await cloudFiles.OpenReadAsync(VerdictsKey);
            return await JsonSerializer.DeserializeAsync<Dictionary<string, MirrorTestVerdict>>(stream, ManifestJson) ?? [];
        }
        catch (Exception ex) when (StorageErrors.IsNotFound(ex))
        {
            return [];
        }
    }

    /// <summary>Keeps how a subscription's test last ran, or forgets it with null. Only the mirror's work writes it, one piece at a time.</summary>
    internal async Task SaveVerdictAsync(string name, MirrorTestVerdict verdict)
    {
        var verdicts = await ReadVerdictsAsync();
        if (verdict == null && !verdicts.Remove(name)) return;
        if (verdict != null) verdicts[name] = verdict;
        await cloudFiles.WriteTextAsync(JsonSerializer.Serialize(verdicts, ManifestJson),
            new WriteFileSettings { Key = VerdictsKey, ContentType = "application/json" });
    }
}
