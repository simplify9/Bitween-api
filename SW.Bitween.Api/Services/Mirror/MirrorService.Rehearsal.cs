using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Cli;
using SW.Bitween.Cli.Configuration;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using SW.Serverless.Contract.Catalog;
using static SW.Bitween.Services.Mirror.MirrorCompare;

namespace SW.Bitween.Services.Mirror;

public sealed partial class MirrorService
{
    /// <summary>How long one exchange may take on staging before the rehearsal moves on without it.</summary>
    static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Prod's setup beside staging's, compared once for every exchange of a rehearsal.</summary>
    /// <param name="Open">The addresses and secrets the copy left empty that staging has no value for yet.</param>
    sealed record Setup(MirrorParity Parity, Plan Plan, MirrorSetupCopy Copy, IReadOnlyList<MirrorBlank> Open);

    /// <summary>Families in the order a rehearsal runs them: as they came in, aggregations last, since they roll up what the rest ran.</summary>
    static List<MirrorFamily> InOrder(IEnumerable<MirrorFamily> families) =>
        (families ?? []).OrderBy(f => f.Entry == "aggregation" ? 1 : 0).ThenBy(f => f.Members[0].StartedOn).ToList();

    /// <summary>
    /// Prod's setup beside staging's, read as whoever started the work, to compare what each exchange used;
    /// or why it couldn't be, which doesn't stop the run.
    /// </summary>
    async Task<(Setup Setup, string Error)> SetupAsync(string selfToken)
    {
        const string why = "What each exchange used isn't compared with prod's setup: ";
        try
        {
            using var comparison = await CompareAsync(removeExtras: false, copyKey: null, selfToken);
            return (new Setup(comparison.Parity, comparison.Plan, comparison.Copy, comparison.Open), null);
        }
        catch (MirrorRefusal refusal)
        {
            return (null, why + refusal.Message);
        }
        catch (BitweenApiException ex) when (ex.Status == null)
        {
            return (null, why + ex.Message);
        }
        catch (Exception ex) when (ex is BitweenApiException or HttpRequestException or TaskCanceledException)
        {
            return (null, $"{why}prod couldn't be read, as {Why(ex)}.");
        }
    }

    /// <summary>
    /// Runs families on staging one at a time, each under the key that lets its traffic through, and
    /// hands each run as it is compared to <paramref name="ran"/>, with the ones before it. Stops when two
    /// in a row don't finish.
    /// </summary>
    async Task<List<MirrorFamilyRun>> RunFamiliesAsync(IReadOnlyList<MirrorFamily> families, Setup setup, Action<int, int> progress,
        Func<List<MirrorFamilyRun>, Task> ran, CancellationToken cancellation)
    {
        var runs = new List<MirrorFamilyRun>();
        var unfinished = 0;
        var key = MirrorTraffic.OpenKey();
        using var doors = new HttpClient(new HttpClientHandler { UseCookies = false })
        {
            BaseAddress = new Uri(SelfUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(2),
        };
        // The bus gateways prod has on, which the copy switched off so nothing runs on its own: the
        // rehearsal's traffic goes through them, as prod's does.
        MirrorTraffic.RouteThrough(await SwitchedOffGatewaysAsync(setup));
        try
        {
            foreach (var family in families)
            {
                cancellation.ThrowIfCancellationRequested();
                var run = await RunAsync(family, setup, new Doors(doors, key, Faker()), cancellation);
                runs.Add(run);
                progress?.Invoke(runs.Count, families.Count);
                await ran(runs.ToList());
                unfinished = run.Members.Any(m => m.StagingExchangeId != null && m.Outcome == null) ? unfinished + 1 : 0;
                if (unfinished == 2)
                    throw new MirrorRefusal($"Two families in a row didn't finish on staging within {RunTimeout.TotalMinutes:0} minutes, so the run stopped. " +
                                            "Open their exchanges on the exchanges page to see where they are.");
            }
        }
        finally
        {
            MirrorTraffic.RouteThrough(null);
            MirrorTraffic.CloseKey(key);
        }
        return runs;
    }

    async Task<IReadOnlySet<int>> SwitchedOffGatewaysAsync(Setup setup)
    {
        const string prefix = "busGateways/";
        var names = (setup?.Copy.SwitchedOff ?? new HashSet<string>()).Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => k[prefix.Length..]).ToList();
        if (names.Count == 0) return new HashSet<int>();
        using var scope = scopes.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Domain.Gateway.BusGateway>().AsNoTracking()
            .Where(g => names.Contains(g.Name)).Select(g => g.Id).ToListAsync()).ToHashSet();
    }

    /// <summary>A family's run as the page shows it: each of staging's files with a link that opens it.</summary>
    MirrorFamilyRun Linked(MirrorFamilyRun run) =>
        run with
        {
            Diffs = run.Diffs ?? [],
            Members = (run.Members ?? []).Select(r => r with { Output = Linked(r.Output), Response = Linked(r.Response), ErrorTypes = r.ErrorTypes ?? [], Diffs = r.Diffs ?? [] }).ToList(),
        };

    // ------------------------------------------------------------------ one exchange

    /// <summary>What a rehearsal calls staging's doors with: the client, the key that marks its calls, and the faker partner keys come from.</summary>
    sealed record Doors(HttpClient Client, string Key, MirrorFaker Faker);

    /// <summary>How a family got in: the door's line, whether the door turned it away, or why it couldn't be sent in at all.</summary>
    /// <param name="Rolled">For an aggregation: what staging rolled up, said the way the first exchange's input line says it.</param>
    sealed record Entered(MirrorDiff Door, bool Refused = false, string NotRun = null, string Rolled = null);

    /// <summary>
    /// One family: sent in the way it came in on prod, run by staging under a correlation of its own, and
    /// each exchange staging ran paired with its twin on prod - the exchange of the same subscription, in
    /// the same order - and compared with it.
    /// </summary>
    async Task<MirrorFamilyRun> RunAsync(MirrorFamily family, Setup setup, Doors doors, CancellationToken cancellation)
    {
        var first = family.Members[0];
        if (family.Entry != "aggregation" && first.Input?.Key == null)
            return new MirrorFamilyRun(family.Id, null, DateTimeOffset.UtcNow, null,
                first.Input == null ? "Prod's first exchange had nothing come in to send." : $"Prod's input isn't kept. {first.Input.NotCopied}", [], []);

        var correlation = MirrorTraffic.NewCorrelation();
        var since = DateTime.UtcNow.AddSeconds(-1);
        List<(Xchange X, XchangeResult R)> ran = [];
        var settled = true;
        var entered = await SendInAsync(family, first, correlation, doors, cancellation);
        if (entered.NotRun == null && !entered.Refused) (ran, settled) = await SettledAsync(correlation, since, cancellation);
        if (entered.NotRun != null) return new MirrorFamilyRun(family.Id, null, DateTimeOffset.UtcNow, null, entered.NotRun, [], []);
        return await CompareFamilyAsync(family, setup, correlation, entered, ran, settled);
    }

    static string NoSubscription(string name) => $"Staging doesn't have the subscription “{name}”: bring it from prod.";

    /// <summary>The first exchange's input, sent in through the same door it came in through on prod.</summary>
    async Task<Entered> SendInAsync(MirrorFamily family, MirrorSample first, string correlation, Doors doors, CancellationToken cancellation)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var input = first.Input?.Key == null ? null : await ReadAsync(first.Input.Key);
        var file = input == null ? null : new XchangeFile(MirrorFiles.TextOf(input), first.Input.Name);
        switch (family.Entry)
        {
            case "document":
            {
                var type = await db.Set<Document>().AsNoTracking().FirstOrDefaultAsync(d => d.Name == family.Door, cancellation);
                if (type == null) return new Entered(null, NotRun: $"Staging doesn't have the information type “{family.Door}”: bring it from prod.");
                await scope.ServiceProvider.GetRequiredService<XchangeService>().SubmitFilterXchange(type.Id, file, ["rehearsal"], correlation);
                return new Entered(new MirrorDiff("entry", "How it came in", Same, $"A document of “{family.Door}”", "The same document, routed by staging"));
            }
            case "gateway":
            case "api":
                return await CallAsync(family, first, input, correlation, doors, db, cancellation);
            case "aggregation":
            {
                var subscription = await db.Subscriptions().AsNoTracking().FirstOrDefaultAsync(s => s.Name == family.Door, cancellation);
                if (subscription == null) return new Entered(null, NotRun: NoSubscription(family.Door));
                // The way prod does it: rolling up what staging ran of the subscription it aggregates.
                await ActivatorUtilities.CreateInstance<AggregationJob>(scope.ServiceProvider)
                    .Execute(new AggregationJobParams(subscription.Id, null, correlation));
                if (await db.Set<Xchange>().AnyAsync(x => x.CorrelationId == correlation, cancellation))
                    return new Entered(new MirrorDiff("entry", "How it came in", Same, "Prod's exchanges, rolled up", "Staging's own, rolled up",
                        "It rolls up what staging ran in this rehearsal, so what came in lists other files than prod's."), Rolled: "Staging's own, listed");
                // Staging ran nothing of it: the files prod rolled up instead, faked, served by staging.
                var rolled = (first.Rolled ?? []).Where(f => f.Key != null).ToList();
                if (rolled.Count == 0)
                    return new Entered(null, NotRun: "Staging had nothing to roll up: none of the exchanges it aggregates ran in this rehearsal, and none of the files prod rolled up is kept.");
                var list = JsonSerializer.Serialize(rolled.Select(f => links.LinkTo(f.Key, forAdapter: true)).ToList());
                await scope.ServiceProvider.GetRequiredService<XchangeService>().CreateXchange(subscription, new XchangeFile(list), ["rehearsal"], correlation);
                await db.SaveChangesAsync(cancellation);
                return new Entered(new MirrorDiff("entry", "How it came in", Info, "Prod's exchanges, rolled up", $"{rolled.Count} of the files prod rolled up, faked",
                    "Staging ran none of the exchanges it aggregates in this rehearsal, so it rolls up a few of the files prod did, as staging keeps them."),
                    Rolled: $"{rolled.Count} of prod's, faked, listed");
            }
            default:
            {
                var subscription = await db.Subscriptions().FirstOrDefaultAsync(s => s.Name == family.Door, cancellation);
                if (subscription == null) return new Entered(null, NotRun: NoSubscription(family.Door));
                var partnerName = family.Partner ?? first.Partner;
                Partner partner = null;
                if (partnerName != null && (partner = await db.Set<Partner>().FirstOrDefaultAsync(p => p.Name == partnerName, cancellation)) == null)
                    return new Entered(null, NotRun: $"Staging doesn't have the partner “{partnerName}”: bring it from prod.");
                await scope.ServiceProvider.GetRequiredService<XchangeService>().CreateXchange(subscription, file, ["rehearsal"], correlation, partner);
                await db.SaveChangesAsync(cancellation);
                return new Entered(family.Entry == "receiver"
                    ? new MirrorDiff("entry", "How it came in", Info, "A file its receiver read", "The same file, handed straight to it",
                        "Reading it from the server comes with the test servers.")
                    : new MirrorDiff("entry", "How it came in", Same, "Started by hand", "Started the same way"));
            }
        }
    }

    /// <summary>
    /// A partner's call through one of staging's doors - an API gateway, or the partner API - as prod's
    /// partner made it: with staging's key for the partner, or a token staging signs for it.
    /// </summary>
    async Task<Entered> CallAsync(MirrorFamily family, MirrorSample first, byte[] input, string correlation, Doors doors, BitweenDbContext db,
        CancellationToken cancellation)
    {
        var name = family.Partner ?? first.Partner;
        var partner = name == null ? null : await db.Set<Partner>().AsNoTracking().Where(p => p.Name == name)
            .Select(p => new { p.Name, p.LoginIdentity, Keys = p.ApiCredentials.Select(c => c.Name).ToList() })
            .FirstOrDefaultAsync(cancellation);
        if (partner == null)
            return new Entered(null, NotRun: name == null ? "Prod's exchange doesn't say which partner called." : $"Staging doesn't have the partner “{name}”: bring it from prod.");

        using var request = new HttpRequestMessage(HttpMethod.Post, (Uri)null);
        string said;
        if (family.Entry == "gateway")
        {
            var gateway = family.Door == null ? null
                : await db.Set<Domain.Gateway.ApiGateway>().AsNoTracking().FirstOrDefaultAsync(g => g.UrlName == family.Door, cancellation);
            if (gateway == null)
                return new Entered(null, NotRun: family.Door == null ? "Prod's setup doesn't say which gateway it came through." : $"Staging doesn't have the gateway “{family.Door}”: bring it from prod.");
            request.RequestUri = new Uri($"api/gateway/{gateway.UrlName}/async", UriKind.Relative);
            said = $"A call to the gateway “{gateway.UrlName}” from “{name}”";
            if (gateway.AuthMethod == GatewayAuthMethod.Jwt)
                return new Entered(null, NotRun: $"The gateway “{gateway.UrlName}” takes tokens from a login server, which the test can't sign: " +
                    "send this one in by hand, with a token from your test login server.");
        }
        else
        {
            request.RequestUri = new Uri($"api/xchanges/{Uri.EscapeDataString(family.Door ?? "")}", UriKind.Relative);
            said = $"A call to the partner API from “{name}”";
        }
        if (request.Headers.Authorization == null)
        {
            if (partner.Keys.Count == 0) return new Entered(null, NotRun: $"Staging's partner “{name}” has no key to call with.");
            if (await StandInKeyAsync(db, partner.Name, doors.Faker, cancellation) is not { } key)
                return new Entered(null, NotRun: $"Staging's partner “{name}” has only keys set on staging, none the copy gave it, so the test can't call as it.");
            request.Headers.Add(PartnerKeyHeaders.Default, key);
        }
        request.Headers.Add(MirrorTraffic.Header, $"{doors.Key} {correlation}");
        request.Content = new ByteArrayContent(input ?? []);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(ContentType(first.Input?.Format));

        using var response = await doors.Client.SendAsync(request, cancellation);
        var status = (int)response.StatusCode;
        if (status is >= 200 and < 300)
            return new Entered(new MirrorDiff("entry", "How it came in", Same, said, $"Taken: staging answered {status}"));
        var body = (await response.Content.ReadAsStringAsync(cancellation)).Trim();
        return new Entered(new MirrorDiff("entry", "How it came in", Different, said, $"Refused: staging answered {status}",
            $"Prod took this call, and staging turned it away{(body.Length == 0 ? "." : $": {(body.Length > 300 ? body[..300] + "…" : body)}")}"), Refused: true);
    }

    /// <summary>
    /// The key a test calls as a partner with: the stand-in a copy gives one of the partner's keys, the same
    /// every time, which is how the test knows it. Every stand-in comes from staging's Token:Key, so a key
    /// the copy gave before that changed no longer is one: it is set to the stand-in again. Keys set on
    /// staging stay as they are. Null when the partner has only those.
    /// </summary>
    internal static async Task<string> StandInKeyAsync(BitweenDbContext db, string partner, MirrorFaker faker, CancellationToken cancellation)
    {
        string StandIn(string keyName) => faker.Secret($"partners/{partner}/key/{keyName}");
        var entity = await db.Set<Partner>().FirstAsync(p => p.Name == partner, cancellation);
        if (entity.ApiCredentials.FirstOrDefault(c => c.Key == PartnerKeyHash.Of(StandIn(c.Name))) is { } current) return StandIn(current.Name);
        // Bitween shows a key's first five characters, which for a stand-in are those of its prefix.
        if (entity.ApiCredentials.FirstOrDefault(c => c.KeyPrefix == MirrorFaker.SecretPrefix[..5]) is not { } stale)
            return null;

        // A key keeps what it holds for as long as its name stays, so it goes first, then comes back as the stand-in.
        var others = entity.ApiCredentials.Where(c => c.Name != stale.Name).Select(c => new ApiCredential(c.Name, c.Key)).ToList();
        entity.SetApiCredentials(others);
        await db.SaveChangesAsync(cancellation);
        entity.SetApiCredentials([.. others, new ApiCredential(stale.Name, StandIn(stale.Name))]);
        await db.SaveChangesAsync(cancellation);
        return StandIn(stale.Name);
    }

    /// <summary>
    /// Each of staging's exchanges paired with its twin on prod - the same subscription, in the order they
    /// started - and compared; an exchange one side ran and the other didn't is a difference of its own.
    /// </summary>
    async Task<MirrorFamilyRun> CompareFamilyAsync(MirrorFamily family, Setup setup, string correlation, Entered entered,
        List<(Xchange X, XchangeResult R)> ran, bool settled)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xchanges = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var subscriptions = await db.Subscriptions().AsNoTracking().Select(s => new { s.Id, s.Name }).ToDictionaryAsync(s => s.Id, s => s.Name);
        var types = await db.Set<Document>().AsNoTracking().Select(d => new { d.Id, d.Name }).ToDictionaryAsync(d => d.Id, d => d.Name);
        string NameOf(Xchange x) => x.SubscriptionId is { } id ? subscriptions.GetValueOrDefault(id) : types.GetValueOrDefault(x.DocumentId);
        // A document being routed has no subscription: it pairs with one of the same type.
        string TwinOf(Xchange x) => x.SubscriptionId == null ? "\u0000" + NameOf(x) : NameOf(x);
        static string Twin(MirrorSample p) => p.Subscription ?? "\u0000" + p.InformationType;

        var pool = ran.ToList();
        var members = new List<MirrorRun>();
        for (var i = 0; i < family.Members.Count; i++)
        {
            var p = family.Members[i];
            var at = pool.FindIndex(s => TwinOf(s.X) == Twin(p));
            if (at < 0)
            {
                members.Add(Missing(p, settled && !entered.Refused, entered.Refused));
                continue;
            }
            var (x, r) = pool[at];
            pool.RemoveAt(at);
            members.Add(await MemberAsync(p, i == 0, x, r, setup, xchanges, entered.Rolled));
        }
        foreach (var (x, r) in pool)
            members.Add(Extra(x, r, NameOf(x), family.Cut > 0 || family.Unlinked));

        var diffs = new List<MirrorDiff>();
        if (entered.Door != null) diffs.Add(entered.Door);
        if (setup != null)
        {
            var (prod, staging) = (setup.Parity.Prod.Version, setup.Parity.Staging.Version);
            diffs.Add(new MirrorDiff("family", "Bitween", prod == staging ? Same : Info, prod, staging, prod == staging ? null : "The release being tried."));
        }
        if (family.Cut > 0)
            diffs.Add(new MirrorDiff("family", "Prod's family", Info, $"More than {family.Members.Count} exchanges", $"{ran.Count} exchanges",
                $"The sample keeps {family.Members.Count} of prod's family's exchanges, so staging's others aren't compared."));
        else if (family.Unlinked)
            diffs.Add(new MirrorDiff("family", "Where it went", Info, "Not recorded", $"{ran.Count} exchanges",
                "It came in on prod without a correlation id, so prod doesn't say which subscriptions it went to: staging's are run, but not counted as more than prod's."));

        var levels = members.Select(m => m.Verdict).Append(entered.Door?.Level).ToList();
        var verdict = levels.Contains(Different) ? Different : levels.Contains(Check) ? Check : Same;
        return new MirrorFamilyRun(family.Id, correlation, DateTimeOffset.UtcNow, verdict, null, diffs, members);
    }

    static string OutcomeOf(XchangeResult r) => !r.Success ? "failed" : r.ResponseBad ? "badResponse" : "success";

    /// <summary>One of prod's exchanges staging ran no twin of.</summary>
    static MirrorRun Missing(MirrorSample p, bool settled, bool refused) =>
        new(p.ProdExchangeId, p.Subscription ?? p.InformationType, null, DateTimeOffset.UtcNow, null, null, [],
            settled || refused ? Different : null,
            refused ? "Staging turned the call away, so it didn't run."
            : settled ? "Staging didn't run it, where prod's family did."
            : $"Staging hadn't run it when the rehearsal stopped waiting, after {RunTimeout.TotalMinutes:0} minutes.",
            null, null,
            settled && !refused
                ? [new MirrorDiff("result", "Result", Different, Ending(p.Outcome, p.Status, p.ErrorTypes ?? []), "Not run",
                    "Staging didn't run it: its routing, or an answer before it, went another way than on prod.")]
                : []);

    /// <summary>One of staging's exchanges that prod's family had no twin of.</summary>
    static MirrorRun Extra(Xchange x, XchangeResult r, string name, bool cut)
    {
        var (status, types) = ErrorOf(r?.Exception);
        var outcome = r == null ? null : OutcomeOf(r);
        return new MirrorRun(null, name, x.Id, DateTimeOffset.UtcNow, outcome, status, types, cut ? Same : Different, null, null, null,
            [new MirrorDiff("result", "Result", cut ? Info : Different, "Not run", outcome == null ? "Didn't finish" : Ending(outcome, status, types),
                cut ? "Prod's family was larger than the sample keeps, so it may well have run this too."
                    : "Prod's family didn't run it: staging's routing, or an answer before it, took it somewhere prod didn't.")]);
    }

    /// <summary>One of the family's exchanges on staging, beside its twin on prod.</summary>
    async Task<MirrorRun> MemberAsync(MirrorSample p, bool first, Xchange x, XchangeResult r, Setup setup, XchangeService xchanges,
        string rolled)
    {
        var name = p.Subscription ?? p.InformationType;
        if (r == null)
            return new MirrorRun(p.ProdExchangeId, name, x.Id, DateTimeOffset.UtcNow, null, null, [], null,
                $"It didn't finish on staging within {RunTimeout.TotalMinutes:0} minutes.", null, null, []);

        var (status, types) = ErrorOf(r.Exception);
        var outcome = OutcomeOf(r);
        var (outputFile, output) = await StagingFileAsync(xchanges, x, r.OutputName, r.OutputSize, XchangeFileType.Output);
        var (responseFile, response) = await StagingFileAsync(xchanges, x, r.ResponseName, r.ResponseSize, XchangeFileType.Response);
        // Staging's partner is the test system the team set, not prod's: its answer explains, and never counts.
        var answer = File("answer", "What it got back", p.Response, await SampleFileAsync(p.Response), response);
        if (answer?.Level is Different or Check)
            answer = answer with { Level = Info, Note = $"Staging's partner is a test system, so its answer isn't prod's partner's. {answer.Note}".Trim() };

        var diffs = new List<MirrorDiff>
        {
            Result(p, outcome, status, types),
            File("output", "What it sent", p.Output, await SampleFileAsync(p.Output), output),
            answer,
        };
        if (!first)
        {
            // Handed on by the exchange before it, so it differs where that one's answer did.
            var (_, input) = await StagingFileAsync(xchanges, x, x.InputName, x.InputSize, XchangeFileType.Input);
            diffs.Add(File("input", "What was handed on to it", p.Input, await SampleFileAsync(p.Input), input));
        }
        else if (rolled != null)
            diffs.Add(new MirrorDiff("input", "What came in", Info, "Prod's exchanges, listed", rolled));
        else
            diffs.Add(new MirrorDiff("input", "What came in", Faked, $"{FormatName(p.Input?.Format)} · prod's, faked", "The same file"));
        if (p.Subscription != null) diffs.AddRange(Used(setup, p.Subscription, p, x, r));
        diffs.RemoveAll(d => d == null);
        return new MirrorRun(p.ProdExchangeId, name, x.Id, DateTimeOffset.UtcNow, outcome, status, types, MirrorCompare.Verdict(diffs), null,
            outputFile, responseFile, diffs);
    }

    /// <summary>
    /// Staging's exchanges of a family, once each has finished and nothing new has started for a few
    /// seconds: an answer handed on, or a message across the bus, starts after its exchange is saved.
    /// What there is when <see cref="RunTimeout"/> runs out, with whether it settled.
    /// </summary>
    async Task<(List<(Xchange X, XchangeResult R)> Ran, bool Settled)> SettledAsync(string correlation, DateTime since, CancellationToken cancellation)
    {
        var until = DateTime.UtcNow + RunTimeout;
        List<(Xchange X, XchangeResult R)> ran = [];
        int quiet = 0, last = -1;
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(500, cancellation);
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var rows = await (from x in db.Set<Xchange>().AsNoTracking()
                              where x.StartedOn >= since && x.CorrelationId == correlation
                              join r in db.Set<XchangeResult>().AsNoTracking() on x.Id equals r.Id into results
                              from r in results.DefaultIfEmpty()
                              select new { x, r }).ToListAsync(cancellation);
            ran = rows.OrderBy(a => a.x.StartedOn).Select(a => (a.x, a.r)).ToList();
            var done = ran.Count > 0 && ran.All(a => a.Item2 != null);
            quiet = done && ran.Count == last ? quiet + 1 : 0;
            last = ran.Count;
            if (quiet >= 6) return (ran, true);
        }
        return (ran, false);
    }

    async Task<(MirrorSampleFile File, byte[] Content)> StagingFileAsync(XchangeService xchanges, Xchange xchange, string name, int size, XchangeFileType type)
    {
        if (xchanges.FileKey(xchange.Id, xchange.FilesPrefix, size, type) is not { } key) return (null, null);
        var content = await ReadAsync(key);
        return (new MirrorSampleFile(key, name, MirrorFiles.FormatOf(content), content.Length, null), content);
    }

    async Task<byte[]> SampleFileAsync(MirrorSampleFile file) => file?.Key == null ? null : await ReadAsync(file.Key);

    async Task<byte[]> ReadAsync(string key)
    {
        await using var stream = await cloudFiles.OpenReadAsync(key);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    // ------------------------------------------------------------------ what it used

    /// <summary>
    /// What the exchange used, prod's beside staging's: the release, the subscription, its information
    /// type, the partner it ran as, the global values it names, and the adapters it ran. Prod's is its
    /// setup today, faked as the copy fakes it; its adapters are the ones its exchange recorded.
    /// </summary>
    static IEnumerable<MirrorDiff> Used(Setup setup, string name, MirrorSample sample, Xchange xchange, XchangeResult result)
    {
        if (setup != null)
        {
            var item = (setup.Copy.Sections.GetValueOrDefault("subscriptions") ?? []).FirstOrDefault(i => (string)i["name"] == name);
            yield return Item(setup, "subscriptions", name, "Subscription");
            if ((string)item?["informationType"] is { } type) yield return Item(setup, "informationTypes", type, "Information type");
            if ((sample.Partner ?? (string)item?["partner"]) is { } partner) yield return Item(setup, "partners", partner, "Partner");
            foreach (var set in GlobalSetsIn(setup, item)) yield return Item(setup, "globalValues", set, "Global values");
        }
        yield return Adapter(setup, "Mapper", sample.Mapper, xchange.MapperId, result.MapperVersion);
        yield return Adapter(setup, "Handler", sample.Handler, xchange.HandlerId, result.HandlerVersion);
    }

    static MirrorDiff Item(Setup setup, string section, string key, string noun)
    {
        var row = setup.Parity.Kinds.FirstOrDefault(k => k.Section == section)?.Items.FirstOrDefault(i => i.Key == key);
        var change = setup.Plan.Changes.FirstOrDefault(c => c.Section == section && c.Key == key && c.Op != Op.Delete);
        var name = $"{noun} “{key}”";
        if (row == null)
            return new MirrorDiff("used", name, Info, "Not on prod any more", "On staging", "Prod's setup no longer has it: the samples are older than that.");
        if (row.State is "missing" or "waiting" || change?.Op == Op.Create)
            return new MirrorDiff("used", name, Info, "Has it", "Doesn't have it", row.Reason ?? "Bring it from prod.", null, section, row.StagingId, row.Type);
        if (change?.Op == Op.Update)
        {
            var fields = SetupFields(change.Want, change.Have?.File, change.Fields ?? []);
            return new MirrorDiff("used", name, Info, $"{fields.Count} different", $"{fields.Count} different",
                "Staging's differs from prod's setup as copied today; copying again brings it in line.", fields, section, row.StagingId, row.Type);
        }
        var copied = (setup.Copy.Sections.GetValueOrDefault(section) ?? []).FirstOrDefault(i => Kinds.Of(section).KeyOf(i) == key);
        var faked = section switch
        {
            "partners" when copied?["properties"] is JsonObject { Count: > 0 } => "Its properties are faked.",
            "globalValues" => "Its values are faked.",
            _ => null,
        };
        var own = setup.Copy.Blanks.Any(b => b.Section == section && b.Key == key) ? "Its addresses and secrets are staging's own." : null;
        return new MirrorDiff("used", name, faked == null ? Same : Faked, "Same", "Same", string.Join(" ", new[] { faked, own }.Where(n => n != null)) is { Length: > 0 } note ? note : null,
            null, section, row.StagingId, row.Type);
    }

    /// <summary>The global values sets a subscription's adapters name in {{globals.set.key}}, as the copy names them.</summary>
    static IEnumerable<string> GlobalSetsIn(Setup setup, JsonObject subscription)
    {
        var values = new[] { "receiver", "validator", "mapper", "handler" }
            .SelectMany(role => subscription?[role]?["properties"] as JsonObject ?? [])
            .Select(p => p.Value is JsonValue v && v.TryGetValue<string>(out var s) ? s : null);
        var sets = (setup.Copy.Sections.GetValueOrDefault("globalValues") ?? []).Select(Kinds.Of("globalValues").KeyOf).Where(k => k != null).ToList();
        return StartupValuesFiller.ReferencesIn(values).Globals
            .Select(g => sets.FirstOrDefault(k => string.Equals(k, g.SetId, StringComparison.OrdinalIgnoreCase)))
            .Where(k => k != null).Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// An adapter the exchange ran, prod's beside staging's: the version prod pinned, or else the one it has
    /// as current today, against the one staging ran. A built-in one comes with the release.
    /// </summary>
    static MirrorDiff Adapter(Setup setup, string role, string prodRef, string stagingRef, string stagingVersion)
    {
        if (prodRef == null && stagingRef == null) return null;
        var (prodId, pinned) = prodRef == null ? (null, null) : AdapterCatalogPaths.Split(prodRef);
        var (stagingId, _) = stagingRef == null ? (null, null) : AdapterCatalogPaths.Split(stagingRef);
        var builtIn = NativeAdapterDiscoveryService.IsNative(prodId ?? stagingId);
        var prodVersion = pinned ?? setup?.Parity.Adapters.FirstOrDefault(a => string.Equals(a.Id, prodId, StringComparison.OrdinalIgnoreCase))?.ProdVersion;
        var same = string.Equals(prodId, stagingId, StringComparison.OrdinalIgnoreCase) &&
                   (builtIn || prodVersion == null || stagingVersion == null || prodVersion == stagingVersion);
        string Shown(string id, string version) =>
            id == null ? "None" : NativeAdapterDiscoveryService.IsNative(id) ? $"{id} (built in)" : version == null ? id : $"{id} {version}";
        return new MirrorDiff("used", role, same ? Same : Info, Shown(prodId, builtIn ? null : prodVersion), Shown(stagingId, builtIn ? null : stagingVersion),
            same || pinned != null || prodVersion == null ? null : "Prod's version is the one it has as current today.");
    }
}
