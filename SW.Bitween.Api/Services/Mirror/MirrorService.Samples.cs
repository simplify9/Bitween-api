using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SW.Bitween.Cli;
using SW.Bitween.Cli.Configuration;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services.Mirror;

public sealed partial class MirrorService
{
    /// <summary>Where staging keeps prod's samples, faked, in its own storage.</summary>
    public const string SamplesPrefix = "mirror/samples";

    internal const string SamplesManifest = SamplesPrefix + "/samples.json";

    static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web);

    static readonly MirrorSamples NoSamples = new("none", null, null, 0, 0, null, []);

    // How many of a subscription's exchanges are kept, by how they ended: most of them what worked, the
    // path prod takes every day; one the partner answered badly; one that failed.
    const int Worked = 3, AnsweredBadly = 1, Failed = 1, PerSubscription = 5;

    /// <summary>
    /// Takes a few of prod's recent exchanges for each of its subscriptions, each with its family - what
    /// came in, and every exchange that led to - into staging's storage: what came in, what Bitween sent,
    /// what the partner answered, and how it ended, every file faked the moment it arrives. Prod is read
    /// gently: one subscription at a time, one page of its exchanges, a family by the indexed time it
    /// started, files one by one. Replaces the samples taken before.
    /// </summary>
    public async Task<MirrorSamples> TakeSamplesAsync(Action<int, int> progress, CancellationToken cancellation = default)
    {
        if (!options.Mirror.Target) throw new MirrorRefusal(NotTarget);
        var startedOn = DateTimeOffset.UtcNow;
        using var prod = Connect();
        await VersionAsync(prod);
        var ctx = new Context(prod);
        var skipped = await ReadAsync(ctx, []);
        if (skipped.TryGetValue("subscriptions", out var why))
            throw new MirrorRefusal($"Prod's subscriptions couldn't be read. {why}");
        var (_, faker) = CopyOf(ctx);

        var subscriptions = (ctx.Current.GetValueOrDefault("subscriptions") ?? []).Where(s => s.Key != null)
            .OrderBy(s => s.Key, StringComparer.Ordinal).ToList();
        using var files = new HttpClient(new ReadOnly(new HttpClientHandler { UseCookies = false })) { Timeout = TimeSpan.FromSeconds(60) };
        var taking = new Taking(prod, ctx, files, faker);
        var taken = new List<MirrorSubscriptionSamples>();
        progress?.Invoke(0, subscriptions.Count);
        foreach (var subscription in subscriptions)
        {
            cancellation.ThrowIfCancellationRequested();
            taken.Add(await SamplesOfAsync(taking, subscription, cancellation));
            progress?.Invoke(taken.Count, subscriptions.Count);
        }

        // A subscription takes part in every family it has an exchange in, not only those its own exchanges led to.
        var families = taking.Families.Values.OrderBy(f => f.Members[0].StartedOn).ToList();
        taken = taken.Select(t =>
        {
            var all = families.Where(f => f.Members.Any(m => m.Subscription == t.Subscription)).Select(f => f.Id).ToList();
            return all.Count == 0 ? t : t with { Families = all, Reason = null };
        }).ToList();
        var samples = new MirrorSamples("done", startedOn, DateTimeOffset.UtcNow, taken.Count, subscriptions.Count, null, taken, families);
        await cloudFiles.WriteTextAsync(JsonSerializer.Serialize(samples, ManifestJson),
            new WriteFileSettings { Key = SamplesManifest, ContentType = "application/json" });
        await RemoveUnusedSamplesAsync(samples);
        return samples;
    }

    /// <summary>The samples staging holds, each file with a link that opens it, and how a taking under way is going.</summary>
    public async Task<MirrorSamples> SamplesAsync(MirrorWork work)
    {
        var samples = await ReadSamplesAsync();
        samples = samples with
        {
            Subscriptions = samples.Subscriptions.Select(s => s with { Families = s.Families ?? [] }).ToList(),
            Families = (samples.Families ?? []).Select(f => f with { Members = f.Members.Select(Linked).ToList() }).ToList(),
        };
        if (work.Running is { Kind: MirrorWork.Sampling } running)
            return samples with { State = "running", StartedOn = running.StartedOn, FinishedOn = null, Done = running.Done, Total = running.Total, Error = null };
        return work.LastError(MirrorWork.Sampling) is { } error ? samples with { State = "failed", Error = error } : samples;
    }

    async Task<MirrorSamples> ReadSamplesAsync()
    {
        try
        {
            await using var stream = await cloudFiles.OpenReadAsync(SamplesManifest);
            return await JsonSerializer.DeserializeAsync<MirrorSamples>(stream, ManifestJson) ?? NoSamples;
        }
        catch (Exception ex) when (StorageErrors.IsNotFound(ex))
        {
            return NoSamples;
        }
    }

    MirrorSample Linked(MirrorSample sample) =>
        sample with
        {
            ErrorTypes = sample.ErrorTypes ?? [], Input = Linked(sample.Input), Output = Linked(sample.Output), Response = Linked(sample.Response),
            Rolled = sample.Rolled?.Select(Linked).ToList(),
        };

    MirrorSampleFile Linked(MirrorSampleFile file) => file?.Key == null ? file : file with { Url = links.LinkTo(file.Key) };

    // ------------------------------------------------------------------ one subscription

    /// <summary>One taking: what it reads prod with, and the families it has found so far, by their first exchange.</summary>
    sealed class Taking(BitweenApi prod, Context ctx, HttpClient files, MirrorFaker faker)
    {
        public BitweenApi Prod { get; } = prod;
        public Context Ctx { get; } = ctx;
        public HttpClient Files { get; } = files;
        public MirrorFaker Faker { get; } = faker;
        public Dictionary<string, MirrorFamily> Families { get; } = new(StringComparer.Ordinal);

        /// <summary>Each exchange already in a family, with the family's id.</summary>
        public Dictionary<string, string> FamilyOf { get; } = new(StringComparer.Ordinal);

        /// <summary>Prod's subscriptions as the copy reads them, by their id on prod.</summary>
        public Dictionary<string, Existing> Subscriptions { get; } = (ctx.Current.GetValueOrDefault("subscriptions") ?? [])
            .Where(s => s.Id != null).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        /// <summary>The same, by name.</summary>
        public Dictionary<string, Existing> ByName { get; } = (ctx.Current.GetValueOrDefault("subscriptions") ?? [])
            .Where(s => s.Key != null).GroupBy(s => s.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    async Task<MirrorSubscriptionSamples> SamplesOfAsync(Taking taking, Existing subscription, CancellationToken cancellation)
    {
        List<JsonObject> rows;
        try
        {
            rows = await RecentAsync(taking.Prod, subscription.Id, DateTime.UtcNow.AddDays(-30));
            if (rows.Count == 0) rows = await RecentAsync(taking.Prod, subscription.Id, since: null);
        }
        catch (Exception ex) when (ex is BitweenApiException or HttpRequestException or TaskCanceledException)
        {
            return new MirrorSubscriptionSamples(subscription.Key, subscription.Id, [], $"Prod didn't list its exchanges: {Why(ex)}.");
        }

        var chosen = Choose(rows);
        if (chosen.Count == 0)
            return new MirrorSubscriptionSamples(subscription.Key, subscription.Id, [],
                rows.Count == 0 ? "Prod has no exchanges of it." : "Prod's exchanges of it are all still running or retries.");
        var families = new List<string>();
        foreach (var row in chosen)
        {
            if (!taking.FamilyOf.TryGetValue(Id(row), out var family))
            {
                if (await FamilyAsync(taking, row, cancellation) is not { } found) continue;
                taking.Families[found.Id] = found;
                foreach (var member in found.Members) taking.FamilyOf.TryAdd(member.ProdExchangeId, found.Id);
                family = found.Id;
            }
            if (!families.Contains(family)) families.Add(family);
        }
        return new MirrorSubscriptionSamples(subscription.Key, subscription.Id, families,
            families.Count == 0 ? "Its handler reads values from the exchange that started it, and prod's exchanges of it don't say which." : null);
    }

    static string Id(JsonObject row) => (string)row["id"] ?? "";

    /// <summary>How many of a family's exchanges a sample keeps: a document can go to many subscriptions.</summary>
    const int FamilyMembers = 25;

    /// <summary>A family's exchanges start this close to its first one: answers and messages are handed on at once.</summary>
    static readonly TimeSpan Before = TimeSpan.FromMinutes(1), After = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The exchange on prod and its family, by the links prod records: up to the exchange whose answer or
    /// message started it, then down to what each one started - the subscriptions a document was routed
    /// to, an answer handed on, a message a bus gateway took - never a retry. A document's subscriptions
    /// share its correlation id and its input, and start after it; an answer to a response subscription,
    /// and a message, name the exchange that handed them on; an answer to a legacy target shares its
    /// correlation id. Each search is bound to the minutes around the exchange it starts from, which
    /// prod has indexed, so a big prod isn't searched end to end. Null for an exchange whose handler reads
    /// values from the exchange that started it, when prod doesn't say which.
    /// </summary>
    async Task<MirrorFamily> FamilyAsync(Taking taking, JsonObject sampled, CancellationToken cancellation)
    {
        var top = sampled;
        for (var depth = 0; depth < 4 && (string)top["sourceXchangeId"] is { Length: > 0 } parentId; depth++)
        {
            if (await ByIdAsync(taking.Prod, parentId) is not { } parent) break;
            top = parent;
        }
        if (SubscriptionOf(taking, top) is { } first && (string)first.File["type"] is "Response" or "BusGateway" && ReadsSource(first)) return null;

        var rows = new List<JsonObject>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new Queue<JsonObject>();
        frontier.Enqueue(top);
        var (cut, unlinked) = (0, false);
        while (frontier.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            var row = frontier.Dequeue();
            if (!seen.Add(Id(row))) continue;
            if (rows.Count == FamilyMembers)
            {
                cut++;
                continue;
            }
            rows.Add(row);
            var around = StartedOn(row);
            var correlation = (string)row["correlationId"];
            var subscription = SubscriptionOf(taking, row);
            if (subscription == null)
            {
                // A document being routed: the subscriptions it went to.
                if (string.IsNullOrEmpty(correlation)) unlinked = true;
                else
                    foreach (var hit in await NearAsync(taking.Prod, $"CorrelationId:1:{correlation}", around))
                        if (hit["subscriptionId"] != null && string.IsNullOrEmpty((string)hit["sourceXchangeId"]) &&
                            StartedOn(hit) >= around && (int?)hit["inputFileSize"] == (int?)row["inputFileSize"])
                            frontier.Enqueue(hit);
                continue;
            }
            if (!string.IsNullOrEmpty((string)subscription.File["responseMessageTypeName"]) ||
                (string)subscription.File["responseSubscription"] is { } target && TypeOf(taking, target) == "Response")
                foreach (var next in await NearAsync(taking.Prod, $"SourceXchangeId:1:{Id(row)}", around))
                    frontier.Enqueue(next);
            else if ((string)subscription.File["responseSubscription"] is { } legacy && !string.IsNullOrEmpty(correlation) &&
                     taking.ByName.GetValueOrDefault(legacy)?.Id is { } legacyId)
                foreach (var next in await NearAsync(taking.Prod, $"CorrelationId:1:{correlation}", around))
                    if (next["subscriptionId"]?.ToString() == legacyId && StartedOn(next) >= around)
                        frontier.Enqueue(next);
        }
        rows = rows.OrderBy(StartedOn).ToList();
        var kept = rows.Select(Id).ToHashSet(StringComparer.Ordinal);

        var members = new List<MirrorSample>();
        foreach (var row in rows)
            members.Add(await SampleAsync(taking, row, kept, cancellation));
        var (entry, door, partner) = EntryOf(taking, rows[0], members[0]);
        return new MirrorFamily(Id(rows[0]), entry, door, partner, members, cut, unlinked);
    }

    /// <summary>Whether a subscription's handler reads values from the exchange that started it: {{source.…}}.</summary>
    static bool ReadsSource(Existing subscription) =>
        StartupValuesFiller.SourcePathsIn((subscription.File["handler"]?["properties"] as JsonObject ?? []).Select(p => p.Value?.ToString())).Any();

    static Existing SubscriptionOf(Taking taking, JsonObject row) =>
        row["subscriptionId"]?.ToString() is { } id ? taking.Subscriptions.GetValueOrDefault(id) : null;

    static string TypeOf(Taking taking, string subscriptionKey) => (string)taking.ByName.GetValueOrDefault(subscriptionKey)?.File["type"];

    /// <summary>
    /// How a family came in on prod, what through, and who called: a document routed (one that isn't
    /// kept as an exchange is seen only in the subscriptions it reached), a call to a gateway or to the
    /// partner API, a file a receiver read, an aggregation, or an exchange started by hand.
    /// </summary>
    static (string Entry, string Door, string Partner) EntryOf(Taking taking, JsonObject first, MirrorSample member)
    {
        if (member.Subscription == null) return ("document", member.InformationType, null);
        var subscription = taking.Subscriptions.GetValueOrDefault(first["subscriptionId"]?.ToString() ?? "");
        switch ((string)subscription?.File["type"])
        {
            case "GatewayApiCall":
                var gateway = (taking.Ctx.Current.GetValueOrDefault("apiGateways") ?? [])
                    .FirstOrDefault(g => (g.File["partners"] as JsonArray ?? []).OfType<JsonObject>()
                        .Any(p => (string)p["subscription"] == member.Subscription && (member.Partner == null || (string)p["partner"] == member.Partner)));
                return ("gateway", gateway?.Key, member.Partner);
            case "ApiCall":
                return ("api", member.InformationType, member.Partner);
            case "Receiving":
                return ("receiver", member.Subscription, null);
            case "Aggregation":
                return ("aggregation", member.Subscription, null);
            case "Internal" when !string.IsNullOrEmpty((string)first["correlationId"]):
                return ("document", member.InformationType, null);
            default:
                return ("direct", member.Subscription, member.Partner);
        }
    }

    static async Task<JsonObject> ByIdAsync(BitweenApi prod, string id)
    {
        var answer = await prod.GetAsync($"api/xchanges?filter={Uri.EscapeDataString($"Id:1:{id}")}&page=0&size=1");
        return (answer?["result"] as JsonArray ?? []).OfType<JsonObject>().FirstOrDefault();
    }

    /// <summary>Prod's exchanges matching <paramref name="filter"/> that started around <paramref name="around"/>, never a retry.</summary>
    static async Task<List<JsonObject>> NearAsync(BitweenApi prod, string filter, DateTime around)
    {
        static string At(DateTime on) => on.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var answer = await prod.GetAsync($"api/xchanges?filter={Uri.EscapeDataString(filter)}" +
                                         $"&filter={Uri.EscapeDataString($"StartedOn:6:{At(around - Before)}")}" +
                                         $"&filter={Uri.EscapeDataString($"StartedOn:8:{At(around + After)}")}&page=0&size=50");
        return (answer?["result"] as JsonArray ?? []).OfType<JsonObject>().Where(r => string.IsNullOrEmpty((string)r["retryFor"])).ToList();
    }

    /// <summary>A page of a subscription's exchanges on prod, newest first: since a day, or ever.</summary>
    static async Task<List<JsonObject>> RecentAsync(BitweenApi prod, string subscriptionId, DateTime? since)
    {
        var query = $"filter={Uri.EscapeDataString($"SubscriptionId:1:{subscriptionId}")}";
        if (since is { } from)
            query += $"&filter={Uri.EscapeDataString($"StartedOn:6:{from.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}")}";
        var answer = await prod.GetAsync($"api/xchanges?{query}&page=0&size=20");
        return (answer?["result"] as JsonArray ?? []).OfType<JsonObject>().ToList();
    }

    /// <summary>
    /// Up to five of a subscription's finished exchanges, newest first and never a retry: up to three
    /// that worked, one the partner answered badly and one that failed, topped up from the rest when one
    /// kind is short. Nothing still running, which has no ending to compare with.
    /// </summary>
    internal static List<JsonObject> Choose(IEnumerable<JsonObject> rows)
    {
        var finished = rows.Where(r => r["status"] is JsonValue && string.IsNullOrEmpty((string)r["retryFor"]))
            .OrderByDescending(StartedOn).ToList();
        var chosen = new List<JsonObject>();
        foreach (var (outcome, most) in new[] { ("success", Worked), ("badResponse", AnsweredBadly), ("failed", Failed) })
            chosen.AddRange(finished.Where(r => Outcome(r) == outcome).Take(most));
        chosen.AddRange(finished.Except(chosen).Take(PerSubscription - chosen.Count));
        return chosen.OrderByDescending(StartedOn).ToList();
    }

    static DateTime StartedOn(JsonObject row) =>
        DateTime.TryParse((string)row["startedOn"], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var on)
            ? on
            : default;

    internal static string Outcome(JsonObject row) =>
        (bool?)row["status"] == false ? "failed" : (bool?)row["responseBad"] == true ? "badResponse" : "success";

    async Task<MirrorSample> SampleAsync(Taking taking, JsonObject row, ISet<string> family, CancellationToken cancellation)
    {
        var id = Id(row);
        var subscription = row["subscriptionId"] is JsonValue sid ? taking.Ctx.KeyOf("subscriptions", sid) : null;
        var type = (string)row["documentName"];
        // Named by staging, never by a path prod could choose: the subscription by a hash of its name, the
        // exchange by its id when that is only letters and digits.
        var folder = $"{SamplesPrefix}/{Folder(subscription ?? type ?? "")}/{(SafeId.IsMatch(id) ? id : Folder(id))}";
        var (status, errorTypes) = ErrorOf((string)row["exception"]);
        var (files, faker) = (taking.Files, taking.Faker);
        var rolled = (string)SubscriptionOf(taking, row)?.File["type"] == "Aggregation"
            ? await RolledAsync(taking, (string)row["inputUrl"], folder, cancellation)
            : null;
        return new MirrorSample(id, StartedOn(row), Outcome(row), status, errorTypes,
            await FileAsync(files, (string)row["inputUrl"], (string)row["inputFileName"], $"{folder}/input", faker, cancellation),
            await FileAsync(files, (string)row["outputUrl"], (string)row["outputFileName"], $"{folder}/output", faker, cancellation),
            await FileAsync(files, (string)row["responseUrl"], (string)row["responseFileName"], $"{folder}/response", faker, cancellation),
            row["partnerId"] is JsonValue partner ? taking.Ctx.KeyOf("partners", partner) : null,
            (string)row["handlerId"], (string)row["mapperId"],
            subscription, type,
            (string)row["sourceXchangeId"] is { } parent && family.Contains(parent) ? parent : null,
            rolled);
    }

    /// <summary>How many of the files an aggregation rolled up a sample keeps.</summary>
    const int RolledFiles = 5;

    /// <summary>
    /// A few of the files an aggregation rolled up on prod, each faked as it arrives: its input lists them.
    /// Read before the input is faked, which would turn the links into test addresses.
    /// </summary>
    async Task<IReadOnlyList<MirrorSampleFile>> RolledAsync(Taking taking, string inputUrl, string folder, CancellationToken cancellation)
    {
        var (files, faker) = (taking.Files, taking.Faker);
        if (string.IsNullOrEmpty(inputUrl)) return [];
        byte[] list;
        try
        {
            list = await DownloadAsync(files, FromProd(inputUrl), cancellation);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            return [];
        }
        List<string> urls;
        try
        {
            urls = list == null ? [] : JsonSerializer.Deserialize<List<string>>(list) ?? [];
        }
        catch (JsonException)
        {
            urls = [];
        }
        finally
        {
            if (list != null) Array.Clear(list);
        }
        var kept = new List<MirrorSampleFile>();
        for (var i = 0; i < Math.Min(RolledFiles, urls.Count); i++)
        {
            var name = $"rolled-{i + 1}";
            kept.Add(await ServedAsync(taking.Prod, urls[i]) is { } link
                ? await FileAsync(files, link, name, $"{folder}/rolled/{i + 1}", faker, cancellation)
                : new MirrorSampleFile(null, faker.FileName(name), null, 0, "It isn't a file prod serves, so staging doesn't fetch it."));
        }
        return kept;
    }

    /// <summary>An exchange file's place in storage: …/{exchange id}/input, output or response.</summary>
    static readonly Regex StoredExchangeFile = new(@"/(?<id>[0-9a-f]{32})/(?<type>input|output|response)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Where staging reads a file an aggregation lists: one of prod's own links, or a storage address of
    /// one of prod's exchanges' files - from before files were private - through prod's link to that
    /// file. Null for any other address: staging never fetches what prod's data merely names.
    /// </summary>
    async Task<string> ServedAsync(BitweenApi prod, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return null;
        if (uri.AbsolutePath.Contains("/" + FileLinks.RoutePrefix + "/", StringComparison.Ordinal)) return FromProd(url);
        if (StoredExchangeFile.Match(uri.AbsolutePath) is not { Success: true } m) return null;
        try
        {
            return await ByIdAsync(prod, m.Groups["id"].Value) is { } row && (string)row[$"{m.Groups["type"].Value.ToLowerInvariant()}Url"] is { } link
                ? FromProd(link)
                : null;
        }
        catch (Exception ex) when (ex is BitweenApiException or HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    static readonly Regex SafeId = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);

    static string Folder(string name) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..16].ToLowerInvariant();

    static readonly Regex StatusPrefix = new(@"^(?<code>[1-5][0-9]{2}): [A-Za-z]+: ", RegexOptions.Compiled);

    // Where an error names its kind: at the start, and after each "--->" of an inner one. Never inside
    // the stack trace, whose frames name methods such as ThrowException.
    static readonly Regex ErrorType = new(@"(?:^|--->)\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)*(?<type>[A-Z][A-Za-z0-9_]*(?:Exception|Error))\b", RegexOptions.Compiled);

    /// <summary>
    /// How prod's exchange ended, from its error: the partner's HTTP status where Bitween wrote it first
    /// ("500: InternalServerError: ..."), and the kinds of error it names, outermost first. Only names
    /// from code: the message itself can hold prod's data, so none of it is kept.
    /// </summary>
    internal static (int? Status, IReadOnlyList<string> Types) ErrorOf(string exception)
    {
        if (string.IsNullOrEmpty(exception)) return (null, []);
        var m = StatusPrefix.Match(exception);
        var types = ErrorType.Matches(exception[m.Length..]).Select(t => t.Groups["type"].Value)
            .Distinct(StringComparer.Ordinal).Take(3).ToList();
        return (m.Success ? int.Parse(m.Groups["code"].Value, CultureInfo.InvariantCulture) : null, types);
    }

    // ------------------------------------------------------------------ one file

    async Task<MirrorSampleFile> FileAsync(HttpClient files, string url, string name, string key, MirrorFaker faker, CancellationToken cancellation)
    {
        if (string.IsNullOrEmpty(url)) return null;
        byte[] content;
        try
        {
            content = await DownloadAsync(files, FromProd(url), cancellation);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            return new MirrorSampleFile(null, faker.FileName(name), null, 0, ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound }
                ? "Prod doesn't have this file any more."
                : $"Prod didn't hand it over: {Why(ex)}.");
        }
        if (content == null) return Kept(null, MirrorFiles.TooLarge(faker.FileName(name)));

        var faked = MirrorFiles.Fake(faker, content, name);
        // Prod's file was only ever here, in memory, until it was faked.
        Array.Clear(content);
        if (faked.Content == null) return Kept(null, faked);
        await cloudFiles.WriteAsync(new MemoryStream(faked.Content), new WriteFileSettings { Key = key, ContentType = ContentType(faked.Format) });
        return Kept(key, faked);
    }

    static MirrorSampleFile Kept(string key, MirrorFakedFile faked) =>
        new(key, faked.Name, faked.Format, faked.Content?.Length ?? 0, faked.NotCopied);

    /// <summary>A file of prod's, or null when it is larger than a sample keeps, which is then left unread.</summary>
    static async Task<byte[]> DownloadAsync(HttpClient files, string url, CancellationToken cancellation)
    {
        using var response = await files.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MirrorFiles.MaxBytes) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        try
        {
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellation)) > 0)
            {
                if (buffer.Length + read > MirrorFiles.MaxBytes) return null;
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        finally
        {
            Array.Clear(chunk);
            Array.Clear(buffer.GetBuffer());
        }
    }

    /// <summary>
    /// One of prod's file links, through the address staging reaches prod on: prod builds them on its own
    /// public address, which staging may reach another way.
    /// </summary>
    string FromProd(string url)
    {
        var source = options.Mirror.SourceUrl.TrimEnd('/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return source + "/" + url.TrimStart('/');
        var at = uri.PathAndQuery.IndexOf("/" + FileLinks.RoutePrefix + "/", StringComparison.Ordinal);
        return at < 0 ? url : source + uri.PathAndQuery[at..];
    }

    static string Why(Exception ex) => ex switch
    {
        BitweenApiException { Status: { } status } => $"it answered {(int)status}",
        HttpRequestException { StatusCode: { } code } => $"it answered {(int)code}",
        TaskCanceledException => "it took too long to answer",
        _ => "it couldn't be reached",
    };

    static string ContentType(string format) => format switch
    {
        "json" => "application/json",
        "xml" => "application/xml",
        "html" => "text/html",
        "csv" => "text/csv",
        "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "text/plain",
    };

    /// <summary>Files of samples taken before that the new ones don't name: staging keeps only the latest.</summary>
    async Task RemoveUnusedSamplesAsync(MirrorSamples samples)
    {
        var kept = (samples.Families ?? []).SelectMany(f => f.Members).SelectMany(s => new[] { s.Input, s.Output, s.Response }.Concat(s.Rolled ?? []))
            .Where(f => f?.Key != null).Select(f => f.Key).Append(SamplesManifest).ToHashSet(StringComparer.Ordinal);
        foreach (var file in await cloudFiles.ListAsync(SamplesPrefix + "/"))
            if (!kept.Contains(file.Key))
                await cloudFiles.DeleteAsync(file.Key);
    }
}
