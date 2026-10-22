using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Connections;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Domain.Gateway;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services.Mirror;

/// <summary>A call as it reached the mirror: from one of Bitween's own adapters, or an adapter's own process.</summary>
public record MirrorRequest(string Method, Uri Url, string ContentType, byte[] Body, string From);

/// <summary>What staging answered a call with, and why that.</summary>
public record MirrorAnswer(int Status, string ContentType, byte[] Body, string Why);

/// <summary>One call staging answered.</summary>
public record MirrorCall(DateTimeOffset At, string Method, string Host, string Path, int Status, string Why, string From);

/// <summary>The calls to one host staging answered, as the page shows them.</summary>
/// <param name="Test">One of prod's addresses as the copy faked it, rather than one staging's setup doesn't name.</param>
public record MirrorHostCalls(string Host, bool Test, int Calls, DateTimeOffset LastAt, string LastWhy, IReadOnlyList<string> UsedBy);

/// <summary>
/// What staging's own setup names, read now and then: the hosts it may call for real, which
/// subscriptions use each test address, and where each login's reply keeps its token.
/// </summary>
public sealed record MirrorSetupHosts(
    IReadOnlySet<string> Named,
    IReadOnlyDictionary<string, IReadOnlyList<string>> UsedBy,
    IReadOnlyDictionary<string, string> TokenPaths,
    IReadOnlySet<string> Logins)
{
    public static readonly MirrorSetupHosts None = new(new HashSet<string>(), new Dictionary<string, IReadOnlyList<string>>(),
        new Dictionary<string, string>(), new HashSet<string>());

    public static async Task<MirrorSetupHosts> ReadAsync(BitweenDbContext db)
    {
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedBy = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var tokenPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var logins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(IEnumerable<KeyValuePair<string, string>> values, string subscription = null)
        {
            foreach (var (key, value) in values ?? [])
            foreach (var host in MirrorFaker.HostsIn(key, value))
            {
                if (!MirrorFaker.IsMockHost(host)) named.Add(host);
                else if (subscription != null)
                    (usedBy.TryGetValue(host, out var names) ? names : usedBy[host] = new SortedSet<string>(StringComparer.Ordinal)).Add(subscription);
            }
        }

        void Login(string url, string tokenPath = null)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var login)) return;
            logins.Add(login.Host + login.AbsolutePath.TrimEnd('/'));
            if (!string.IsNullOrWhiteSpace(tokenPath)) tokenPaths[login.Host] = tokenPath.Trim();
        }

        foreach (var s in await db.Subscriptions().AsNoTracking()
                     .Select(s => new { s.Name, s.HandlerProperties, s.ReceiverProperties, s.MapperProperties, s.ValidatorProperties }).ToListAsync())
        {
            foreach (var properties in new[] { s.HandlerProperties, s.ReceiverProperties, s.MapperProperties, s.ValidatorProperties })
            {
                Add(properties, s.Name);
                if (Get(properties, "LoginUrl") is { } login) Login(login, Get(properties, "LoginTokenPath"));
            }
        }
        foreach (var n in await db.Set<Notifier>().AsNoTracking().Select(n => n.HandlerProperties).ToListAsync()) Add(n);
        foreach (var c in await db.Set<NotificationChannel>().AsNoTracking().Select(c => c.HandlerProperties).ToListAsync()) Add(c);
        foreach (var c in await db.Set<Connection>().AsNoTracking().Select(c => new { c.AuthorizeUrl, c.TokenUrl }).ToListAsync())
        {
            Add([new("authorizeUrl", c.AuthorizeUrl), new("tokenUrl", c.TokenUrl)]);
            Login(c.TokenUrl);
        }
        foreach (var d in await db.Set<DataSource>().AsNoTracking().Select(d => d.Properties).ToListAsync()) Add(d);
        foreach (var b in await db.Set<BusGateway>().AsNoTracking().Select(b => b.EndpointProperties).ToListAsync()) Add(b);
        // What a {{globals...}} or {{partner...}} token stands for is an address like any written out.
        foreach (var g in await db.Set<GlobalAdapterValuesSet>().AsNoTracking().Select(g => g.Values).ToListAsync()) Add(g);
        foreach (var p in await db.Set<Partner>().AsNoTracking().Select(p => p.AdapterProperties).ToListAsync()) Add(p);

        return new MirrorSetupHosts(named, usedBy.ToDictionary(u => u.Key, u => (IReadOnlyList<string>)u.Value.ToList(), StringComparer.OrdinalIgnoreCase),
            tokenPaths, logins);

        static string Get(IReadOnlyDictionary<string, string> properties, string name) =>
            properties?.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value is { Length: > 0 } value ? value : null;
    }
}

/// <summary>
/// On a mirror, answers the calls flows make that staging must not send on: to a test address - one
/// of prod's, as the copy faked it - and to any host staging's own setup doesn't name, such as one
/// written inside an adapter's code. A host the setup names, like a team's own staging API, is called
/// for real, and so is one listed in <c>Bitween:Mirror:PassHosts</c>.
/// <para>
/// A login is answered with a token of staging's own, where the login's settings say its reply keeps
/// one. Anything else gets the newest answer prod's partner gave a subscription that uses the address,
/// as staging keeps it in the samples - faked - or an empty one when there is none. Every answer is
/// counted, so the page can show what was caught.
/// </para>
/// </summary>
public sealed class MirrorMock(IServiceScopeFactory scopes, BitweenOptions options, ILogger<MirrorMock> logger)
{
    static readonly TimeSpan Fresh = TimeSpan.FromMinutes(1);
    const int Kept = 200;

    readonly SemaphoreSlim reading = new(1, 1);
    MirrorSetupHosts setup;
    MirrorSamples samples;
    DateTimeOffset readOn;

    readonly ConcurrentDictionary<string, (byte[] Body, string ContentType)> answers = new(StringComparer.Ordinal);
    readonly ConcurrentQueue<MirrorCall> recent = new();
    readonly ConcurrentDictionary<string, (int Calls, DateTimeOffset LastAt, string LastWhy)> byHost = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Overrides the setup and samples it reads, for tests.</summary>
    internal Func<Task<(MirrorSetupHosts, MirrorSamples)>> ReadOverride { get; set; }

    /// <summary>Overrides how a sample's file is read from storage, for tests.</summary>
    internal Func<string, Task<byte[]>> FileOverride { get; set; }

    /// <summary>Whether staging answers a call to <paramref name="host"/> itself rather than sending it on.</summary>
    public async Task<bool> CatchesAsync(string host)
    {
        var h = (host ?? "").Trim().Trim('[', ']').TrimEnd('.').ToLowerInvariant();
        if (MirrorFaker.IsMockHost(h)) return true;
        if (h is "localhost" || h.EndsWith(".localhost", StringComparison.Ordinal) || IPAddress.TryParse(h, out var ip) && IPAddress.IsLoopback(ip))
            return false;
        if (Own(h) || PassHosts().Contains(h)) return false;
        return !(await ReadAsync()).Setup.Named.Contains(h);
    }

    public async Task<MirrorAnswer> AnswerAsync(MirrorRequest request)
    {
        MirrorAnswer answer;
        try
        {
            var (setup, samples) = await ReadAsync();
            var host = request.Url.Host.ToLowerInvariant();
            answer = AsksForToken(request, setup) ? Token(setup.TokenPaths.GetValueOrDefault(host))
                : await SampleAnswerAsync(host, setup, samples) ?? new MirrorAnswer(200, null, [],
                    "Nothing of prod's to answer with, so an empty reply.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The prod mirror couldn't answer a call to {Host}", request.Url.Host);
            answer = new MirrorAnswer(500, "text/plain", Encoding.UTF8.GetBytes("The prod mirror couldn't answer this call."),
                "Staging couldn't answer it: see the log.");
        }
        Record(request, answer);
        return answer;
    }

    /// <summary>The calls answered since this Bitween started, by host, busiest first.</summary>
    public async Task<IReadOnlyList<MirrorHostCalls>> HostsAsync()
    {
        var setup = (await ReadAsync()).Setup;
        return byHost.Select(h => new MirrorHostCalls(h.Key, MirrorFaker.IsMockHost(h.Key), h.Value.Calls, h.Value.LastAt, h.Value.LastWhy,
                setup.UsedBy.GetValueOrDefault(h.Key) ?? []))
            .OrderByDescending(h => h.Calls).ThenBy(h => h.Host, StringComparer.Ordinal).ToList();
    }

    /// <summary>The latest calls answered, newest first.</summary>
    public IReadOnlyList<MirrorCall> Recent => recent.Reverse().ToList();

    // ------------------------------------------------------------------ answers

    /// <summary>
    /// A login: a URL staging's setup logs in at, or a POST that reads like one - to a path ending in
    /// token or login, or carrying a grant type.
    /// </summary>
    internal static bool AsksForToken(MirrorRequest request, MirrorSetupHosts setup)
    {
        if (!string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase)) return false;
        var path = request.Url.AbsolutePath.TrimEnd('/');
        if (setup.Logins.Contains(request.Url.Host + path)) return true;
        var last = path[(path.LastIndexOf('/') + 1)..].ToLowerInvariant();
        if (last is "token" or "login" or "signin" or "authenticate" or "auth") return true;
        var body = request.Body is { Length: > 0 and <= 16 * 1024 } ? Encoding.UTF8.GetString(request.Body) : "";
        return body.Contains("grant_type", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A token of staging's own, under every name a login's reply commonly gives it, and at the path the
    /// login's settings read it from when they name one ("data.session.token").
    /// </summary>
    internal static MirrorAnswer Token(string path)
    {
        var token = "mirror-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var reply = new JsonObject
        {
            ["access_token"] = token, ["token_type"] = "Bearer", ["expires_in"] = 3600,
            ["token"] = token, ["accessToken"] = token, ["jwt"] = token,
        };
        var segments = (path ?? "").Trim().TrimStart('$').TrimStart('.').Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0 && segments.All(s => !s.Contains('[')))
        {
            var at = reply;
            foreach (var segment in segments[..^1])
                at = at[segment] as JsonObject ?? (JsonObject)(at[segment] = new JsonObject());
            at[segments[^1]] = token;
        }
        return new MirrorAnswer(200, "application/json", Encoding.UTF8.GetBytes(reply.ToJsonString()), "A login, answered with a token of staging's own.");
    }

    /// <summary>The newest answer prod's partner gave, faked, to a subscription that uses the address and worked on prod.</summary>
    async Task<MirrorAnswer> SampleAnswerAsync(string host, MirrorSetupHosts setup, MirrorSamples samples)
    {
        var users = setup.UsedBy.GetValueOrDefault(host);
        if (users == null || samples == null) return null;
        var candidate = samples.Subscriptions.Where(s => users.Contains(s.Subscription))
            .SelectMany(s => s.Samples.Select(x => (s.Subscription, Sample: x)))
            .Where(x => x.Sample.Outcome == "success" && x.Sample.Response?.Key != null)
            .OrderByDescending(x => x.Sample.StartedOn)
            .FirstOrDefault();
        if (candidate.Sample == null) return null;

        var file = candidate.Sample.Response;
        if (!answers.TryGetValue(file.Key, out var cached))
        {
            var body = await ReadFileAsync(file.Key);
            cached = (body, ContentTypeOf(file.Format));
            if (answers.Count < Kept) answers[file.Key] = cached;
        }
        return new MirrorAnswer(200, cached.ContentType, cached.Body, $"Prod's answer to “{candidate.Subscription}”, faked.");
    }

    static string ContentTypeOf(string format) => format switch
    {
        "json" => "application/json",
        "xml" => "application/xml",
        "html" => "text/html",
        "csv" => "text/csv",
        _ => "text/plain",
    };

    async Task<byte[]> ReadFileAsync(string key)
    {
        if (FileOverride != null) return await FileOverride(key);
        using var scope = scopes.CreateScope();
        await using var stream = await scope.ServiceProvider.GetRequiredService<ICloudFilesService>().OpenReadAsync(key);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    // ------------------------------------------------------------------ what it knows

    async Task<(MirrorSetupHosts Setup, MirrorSamples Samples)> ReadAsync()
    {
        if (setup != null && DateTimeOffset.UtcNow - readOn < Fresh) return (setup, samples);
        await reading.WaitAsync();
        try
        {
            if (setup != null && DateTimeOffset.UtcNow - readOn < Fresh) return (setup, samples);
            if (ReadOverride != null) (setup, samples) = await ReadOverride();
            else
            {
                using var scope = scopes.CreateScope();
                setup = await MirrorSetupHosts.ReadAsync(scope.ServiceProvider.GetRequiredService<BitweenDbContext>());
                samples = await ReadSamplesAsync(scope.ServiceProvider.GetRequiredService<ICloudFilesService>());
            }
            readOn = DateTimeOffset.UtcNow;
            answers.Clear();
            return (setup, samples);
        }
        finally
        {
            reading.Release();
        }
    }

    static async Task<MirrorSamples> ReadSamplesAsync(ICloudFilesService files)
    {
        try
        {
            await using var stream = await files.OpenReadAsync(MirrorService.SamplesManifest);
            return await JsonSerializer.DeserializeAsync<MirrorSamples>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (Exception ex) when (StorageErrors.IsNotFound(ex))
        {
            return null;
        }
    }

    /// <summary>The hosts this Bitween is reached on, which adapters call it back on.</summary>
    bool Own(string host) =>
        Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var own) && string.Equals(own.Host, host, StringComparison.OrdinalIgnoreCase);

    HashSet<string> PassHosts() =>
        (options.Mirror.PassHosts ?? "").Split(['\n', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(h => h.Trim('[', ']').ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A call to <paramref name="host"/> that failed before staging could answer it, at TLS: most often
    /// the adapter doesn't trust staging's test authority, or checks certificates its own way.
    /// </summary>
    public void Refused(string host, string why) =>
        Record(new MirrorRequest("CONNECT", new Uri($"https://{host ?? "unknown"}/"), null, [], "An adapter's process"),
            new MirrorAnswer(0, null, [], $"The adapter's call failed before staging could answer: {why}."));

    void Record(MirrorRequest request, MirrorAnswer answer)
    {
        var now = DateTimeOffset.UtcNow;
        recent.Enqueue(new MirrorCall(now, request.Method, request.Url.Host, request.Url.AbsolutePath, answer.Status, answer.Why, request.From));
        while (recent.Count > Kept && recent.TryDequeue(out _)) { }
        byHost.AddOrUpdate(request.Url.Host, _ => (1, now, answer.Why), (_, h) => (h.Calls + 1, now, answer.Why));
    }
}
