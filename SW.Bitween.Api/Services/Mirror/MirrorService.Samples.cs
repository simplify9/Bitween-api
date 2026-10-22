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
    /// Takes a few of prod's recent exchanges for each of its subscriptions - what came in, what Bitween
    /// sent, what the partner answered, and how it ended - into staging's storage, every file faked the
    /// moment it arrives. Prod is read gently: one subscription at a time, one page of its exchanges,
    /// files one by one. Replaces the samples taken before.
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
        var taken = new List<MirrorSubscriptionSamples>();
        progress?.Invoke(0, subscriptions.Count);
        foreach (var subscription in subscriptions)
        {
            cancellation.ThrowIfCancellationRequested();
            taken.Add(await SamplesOfAsync(prod, files, subscription, faker, cancellation));
            progress?.Invoke(taken.Count, subscriptions.Count);
        }

        var samples = new MirrorSamples("done", startedOn, DateTimeOffset.UtcNow, taken.Count, subscriptions.Count, null, taken);
        await cloudFiles.WriteTextAsync(JsonSerializer.Serialize(samples, ManifestJson),
            new WriteFileSettings { Key = SamplesManifest, ContentType = "application/json" });
        await RemoveUnusedSamplesAsync(samples);
        return samples;
    }

    /// <summary>The samples staging holds, each file with a link that opens it, and how a taking under way is going.</summary>
    public async Task<MirrorSamples> SamplesAsync(MirrorSampler sampler)
    {
        var samples = await ReadSamplesAsync();
        samples = samples with
        {
            Subscriptions = samples.Subscriptions.Select(s => s with { Samples = s.Samples.Select(Linked).ToList() }).ToList(),
        };
        if (sampler.Running is { } running)
            return samples with { State = "running", StartedOn = running.StartedOn, FinishedOn = null, Done = running.Done, Total = running.Total, Error = null };
        return sampler.LastError is { } error ? samples with { State = "failed", Error = error } : samples;
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
        sample with { ErrorTypes = sample.ErrorTypes ?? [], Input = Linked(sample.Input), Output = Linked(sample.Output), Response = Linked(sample.Response) };

    MirrorSampleFile Linked(MirrorSampleFile file) => file?.Key == null ? file : file with { Url = links.LinkTo(file.Key) };

    // ------------------------------------------------------------------ one subscription

    async Task<MirrorSubscriptionSamples> SamplesOfAsync(BitweenApi prod, HttpClient files, Existing subscription, MirrorFaker faker,
        CancellationToken cancellation)
    {
        List<JsonObject> rows;
        try
        {
            rows = await RecentAsync(prod, subscription.Id, DateTime.UtcNow.AddDays(-30));
            if (rows.Count == 0) rows = await RecentAsync(prod, subscription.Id, since: null);
        }
        catch (Exception ex) when (ex is BitweenApiException or HttpRequestException or TaskCanceledException)
        {
            return new MirrorSubscriptionSamples(subscription.Key, subscription.Id, [], $"Prod didn't list its exchanges: {Why(ex)}.");
        }

        var chosen = Choose(rows);
        if (chosen.Count == 0)
            return new MirrorSubscriptionSamples(subscription.Key, subscription.Id, [],
                rows.Count == 0 ? "Prod has no exchanges of it." : "Prod's exchanges of it are all still running or retries.");
        var samples = new List<MirrorSample>();
        foreach (var row in chosen)
            samples.Add(await SampleAsync(files, subscription.Key, row, faker, cancellation));
        return new MirrorSubscriptionSamples(subscription.Key, subscription.Id, samples, null);
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

    async Task<MirrorSample> SampleAsync(HttpClient files, string subscription, JsonObject row, MirrorFaker faker, CancellationToken cancellation)
    {
        var id = (string)row["id"] ?? "";
        // Named by staging, never by a path prod could choose: the subscription by a hash of its name, the
        // exchange by its id when that is only letters and digits.
        var folder = $"{SamplesPrefix}/{Folder(subscription)}/{(SafeId.IsMatch(id) ? id : Folder(id))}";
        var (status, errorTypes) = ErrorOf((string)row["exception"]);
        return new MirrorSample(id, StartedOn(row), Outcome(row), status, errorTypes,
            await FileAsync(files, (string)row["inputUrl"], (string)row["inputFileName"], $"{folder}/input", faker, cancellation),
            await FileAsync(files, (string)row["outputUrl"], (string)row["outputFileName"], $"{folder}/output", faker, cancellation),
            await FileAsync(files, (string)row["responseUrl"], (string)row["responseFileName"], $"{folder}/response", faker, cancellation));
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
        var kept = samples.Subscriptions.SelectMany(s => s.Samples).SelectMany(s => new[] { s.Input, s.Output, s.Response })
            .Where(f => f?.Key != null).Select(f => f.Key).Append(SamplesManifest).ToHashSet(StringComparer.Ordinal);
        foreach (var file in await cloudFiles.ListAsync(SamplesPrefix + "/"))
            if (!kept.Contains(file.Key))
                await cloudFiles.DeleteAsync(file.Key);
    }
}
