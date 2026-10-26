using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Connections;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Domain.Gateway;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services.Mirror;

/// <summary>A call as it reached the mirror: from one of Bitween's own adapters, or an adapter's own process.</summary>
public record MirrorRequest(string Method, Uri Url, string ContentType, byte[] Body, string From);

/// <summary>What staging answered a call with, and why that.</summary>
/// <param name="Fail">The call fails as if it never got through, as prod's did in an exchange being rehearsed.</param>
public record MirrorAnswer(int Status, string ContentType, byte[] Body, string Why, bool Fail = false);

/// <summary>One call staging answered, or sent on for real while a rehearsal ran an exchange.</summary>
/// <param name="Real">Sent on for real rather than answered: only noted for a rehearsal.</param>
/// <param name="Exchange">The rehearsal's exchange that made it, where staging can tell.</param>
public record MirrorCall(DateTimeOffset At, string Method, string Host, string Path, int Status, string Why, string From, bool Real = false,
    string Exchange = null);

/// <summary>What prod's partner answered one of a family's exchanges, for staging to answer its twin with.</summary>
public record MirrorReplayAnswer(string Subscription, MirrorAnswer Answer);

/// <summary>The calls to one host staging answered, as the page shows them.</summary>
/// <param name="Test">One of prod's addresses as the copy faked it, rather than one staging's setup doesn't name.</param>

/// <summary>
/// What staging's own setup names, read now and then: the hosts it may call for real, which
/// subscriptions use each test address, and where each login's reply keeps its token.
/// </summary>
public sealed record MirrorSetupHosts(
    IReadOnlySet<string> Named,
    IReadOnlyDictionary<string, IReadOnlyList<string>> UsedBy,
    IReadOnlyDictionary<string, string> TokenPaths,
    IReadOnlySet<string> Logins,
    IReadOnlyDictionary<int, string> Subscriptions = null)
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

        var names = new Dictionary<int, string>();
        foreach (var s in await db.Subscriptions().AsNoTracking()
                     .Select(s => new { s.Id, s.Name, s.HandlerProperties, s.ReceiverProperties, s.MapperProperties, s.ValidatorProperties }).ToListAsync())
        {
            names[s.Id] = s.Name;
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
            tokenPaths, logins, names);

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
/// one. While a rehearsal runs one of prod's exchanges, its subscription's calls get what prod's partner
/// answered in that exchange. Anything else gets the newest answer prod's partner gave a subscription
/// that uses the address, among the arrivals its test took - faked - or an empty one when there is none.
/// </para>
/// </summary>
public sealed class MirrorMock(IServiceScopeFactory scopes, BitweenOptions options, ILogger<MirrorMock> logger)
{
    static readonly TimeSpan Fresh = TimeSpan.FromMinutes(1);
    const int Kept = 200;

    readonly SemaphoreSlim reading = new(1, 1);
    MirrorSetupHosts setup;
    DateTimeOffset readOn;

    readonly ConcurrentDictionary<string, (byte[] Body, string ContentType)> answers = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, IReadOnlyList<MirrorFamily>> arrivals = new(StringComparer.Ordinal);

    sealed class Replaying(string correlation, IReadOnlyList<MirrorReplayAnswer> answers)
    {
        public string Correlation { get; } = correlation;
        public ConcurrentQueue<MirrorCall> Calls { get; } = new();

        readonly Dictionary<string, MirrorReplayAnswer> byExchange = new(StringComparer.Ordinal);
        readonly HashSet<MirrorReplayAnswer> given = [];

        /// <summary>
        /// The answer for a staging exchange of <paramref name="subscription"/>: the first of prod's twins
        /// of it not given to another yet, and the same one for every call it makes.
        /// </summary>
        public MirrorReplayAnswer For(string exchange, string subscription)
        {
            lock (given)
            {
                if (byExchange.TryGetValue(exchange, out var known)) return known;
                var next = answers.FirstOrDefault(a => a.Subscription == subscription && !given.Contains(a));
                if (next != null) given.Add(next);
                return byExchange[exchange] = next;
            }
        }

        public IEnumerable<MirrorReplayAnswer> Of(string subscription) => answers.Where(a => a.Subscription == subscription);
    }

    volatile Replaying replaying;

    /// <summary>
    /// While a rehearsal runs one of prod's families on staging, under <paramref name="correlation"/>:
    /// each exchange's calls are answered with what prod's partner answered its twin - the exchange of
    /// the same subscription, in the same order. A call to an address only other subscriptions use keeps
    /// its usual answer. Every call is noted, those sent on for real too, until <see cref="EndReplay"/>.
    /// </summary>
    public void Replay(string correlation, IReadOnlyList<MirrorReplayAnswer> answers) => replaying = new Replaying(correlation, answers);

    /// <summary>Ends a replay, with the calls made during it.</summary>
    public IReadOnlyList<MirrorCall> EndReplay() => Interlocked.Exchange(ref replaying, null)?.Calls.ToList() ?? [];

    /// <summary>The calls made so far during a replay, for tests.</summary>
    internal IReadOnlyList<MirrorCall> CallsSoFar => replaying?.Calls.ToList() ?? [];

    /// <summary>
    /// The rehearsal exchange a call is for: the one Bitween runs the adapter of itself, or else, for an
    /// adapter's own process, the one the replay's family is running - by the address, when several are.
    /// </summary>
    static MirrorTraffic.Running Making(Replaying r, string host, MirrorSetupHosts setup)
    {
        if (MirrorTraffic.Current is { } current && current.Correlation == r.Correlation) return current;
        var now = MirrorTraffic.Now.Where(x => x.Correlation == r.Correlation).ToList();
        if (now.Count <= 1) return now.FirstOrDefault();
        var users = setup.UsedBy.GetValueOrDefault(host) ?? [];
        return now.FirstOrDefault(x => x.SubscriptionId is { } id && setup.Subscriptions?.GetValueOrDefault(id) is { } name && users.Contains(name))
               ?? now[0];
    }

    /// <summary>Overrides the setup it reads, for tests.</summary>
    internal Func<Task<MirrorSetupHosts>> ReadOverride { get; set; }

    /// <summary>Overrides the arrivals a subscription's test took, for tests.</summary>
    internal Func<string, Task<IReadOnlyList<MirrorFamily>>> ArrivalsOverride { get; set; }

    /// <summary>Overrides how a sample's file is read from storage, for tests.</summary>
    internal Func<string, Task<byte[]>> FileOverride { get; set; }

    /// <summary>Whether staging answers a call to <paramref name="host"/> itself rather than sending it on.</summary>
    public async Task<bool> CatchesAsync(string host) => await PassesAsync(host) == null;

    /// <summary>Why a call to <paramref name="host"/> is sent on for real, in words; null when staging answers it.</summary>
    public async Task<string> PassesAsync(string host)
    {
        var h = (host ?? "").Trim().Trim('[', ']').TrimEnd('.').ToLowerInvariant();
        if (MirrorFaker.IsMockHost(h)) return null;
        if (h is "localhost" || h.EndsWith(".localhost", StringComparison.Ordinal) || IPAddress.TryParse(h, out var ip) && IPAddress.IsLoopback(ip))
            return "it is this machine";
        if (Own(h)) return "it is staging itself";
        if (PassHosts().Contains(h)) return "it is in Hosts called for real";
        return (await ReadAsync()).Named.Contains(h) ? "staging's setup names it" : null;
    }

    /// <summary>A call sent on for real: noted while a rehearsal runs an exchange, where it explains what came back.</summary>
    public void Passed(string method, string host, string path, string why, string from)
    {
        if (replaying is { } r)
            r.Calls.Enqueue(new MirrorCall(DateTimeOffset.UtcNow, method, host, path, 0, $"Sent for real: {why}.", from, Real: true,
                Exchange: Making(r, host, setup ?? MirrorSetupHosts.None)?.Id));
    }

    public async Task<MirrorAnswer> AnswerAsync(MirrorRequest request)
    {
        MirrorAnswer answer;
        try
        {
            var setup = await ReadAsync();
            var host = request.Url.Host.ToLowerInvariant();
            answer = LoginServer(request) ?? (AsksForToken(request, setup) ? Token(setup.TokenPaths.GetValueOrDefault(host)) : null)
                ?? Replayed(host, setup) ?? await SampleAnswerAsync(host, setup) ?? new MirrorAnswer(200, null, [],
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

    MirrorAnswer Replayed(string host, MirrorSetupHosts setup)
    {
        if (replaying is not { } r) return null;
        var making = Making(r, host, setup);
        var name = making?.SubscriptionId is { } id ? setup.Subscriptions?.GetValueOrDefault(id) : null;
        if (name == null) return null;
        // An address only other subscriptions use keeps its usual answer.
        if (setup.UsedBy.TryGetValue(host, out var users) && users.Count > 0 && !users.Contains(name)) return null;
        return r.For(making.Id, name)?.Answer;
    }

    // ------------------------------------------------------------------ a login server for gateways

    const string DiscoveryPath = "/.well-known/openid-configuration", KeysPath = "/.well-known/mirror-keys", KeyId = "bitween-mirror";

    readonly RSA signing = RSA.Create(2048);

    /// <summary>
    /// A token as a partner's login server issues it, for a gateway that checks tokens: signed with a key
    /// of staging's own, which staging hands out as the keys of any test address. So a rehearsal calls a
    /// JWT gateway as prod's partner did, and the gateway checks the token as it does on prod.
    /// </summary>
    public string SignToken(string issuer, string audience, string claim, string value) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = new Dictionary<string, object> { [string.IsNullOrWhiteSpace(claim) ? "sub" : claim] = value },
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(signing) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256),
        });

    /// <summary>A login server's settings and keys, for a test address a gateway trusts; null for any other call.</summary>
    MirrorAnswer LoginServer(MirrorRequest request)
    {
        if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase)) return null;
        var path = request.Url.AbsolutePath;
        JsonObject reply;
        if (path.EndsWith(DiscoveryPath, StringComparison.Ordinal))
        {
            var issuer = request.Url.GetLeftPart(UriPartial.Authority) + path[..^DiscoveryPath.Length];
            reply = new JsonObject
            {
                ["issuer"] = issuer, ["jwks_uri"] = issuer + KeysPath,
                ["id_token_signing_alg_values_supported"] = new JsonArray("RS256"),
                ["response_types_supported"] = new JsonArray("code"), ["subject_types_supported"] = new JsonArray("public"),
            };
        }
        else if (path.EndsWith(KeysPath, StringComparison.Ordinal))
        {
            var key = signing.ExportParameters(false);
            reply = new JsonObject
            {
                ["keys"] = new JsonArray(new JsonObject
                {
                    ["kty"] = "RSA", ["use"] = "sig", ["alg"] = "RS256", ["kid"] = KeyId,
                    ["n"] = Base64UrlEncoder.Encode(key.Modulus), ["e"] = Base64UrlEncoder.Encode(key.Exponent),
                }),
            };
        }
        else return null;
        return new MirrorAnswer(200, "application/json", Encoding.UTF8.GetBytes(reply.ToJsonString()),
            "A login server's keys, answered by staging, which signs the tokens a rehearsal calls gateways with.");
    }

    /// <summary>
    /// The newest answer prod's partner gave, faked, to a subscription that uses the address, in an exchange
    /// that worked on prod: among the arrivals those subscriptions' tests took.
    /// </summary>
    async Task<MirrorAnswer> SampleAnswerAsync(string host, MirrorSetupHosts setup)
    {
        var users = setup.UsedBy.GetValueOrDefault(host);
        if (users == null) return null;
        var members = new List<MirrorSample>();
        foreach (var user in users)
            members.AddRange((await ArrivalsAsync(user)).SelectMany(f => f.Members));
        var candidate = members
            .Where(x => x.Subscription != null && users.Contains(x.Subscription) && x.Outcome == "success" && x.Response?.Key != null)
            .OrderByDescending(x => x.StartedOn)
            .FirstOrDefault();
        if (candidate == null) return null;

        var file = candidate.Response;
        if (!answers.TryGetValue(file.Key, out var cached))
        {
            var body = await ReadFileAsync(file.Key);
            cached = (body, ContentTypeOf(file.Format));
            if (answers.Count < Kept) answers[file.Key] = cached;
        }
        return new MirrorAnswer(200, cached.ContentType, cached.Body, $"Prod's answer to “{candidate.Subscription}”, faked.");
    }

    async Task<IReadOnlyList<MirrorFamily>> ArrivalsAsync(string subscription)
    {
        if (arrivals.TryGetValue(subscription, out var known)) return known;
        IReadOnlyList<MirrorFamily> read;
        if (ArrivalsOverride != null) read = await ArrivalsOverride(subscription);
        else
        {
            using var scope = scopes.CreateScope();
            read = await MirrorService.TestArrivalsAsync(scope.ServiceProvider.GetRequiredService<ICloudFilesService>(), subscription);
        }
        return arrivals[subscription] = read;
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

    async Task<MirrorSetupHosts> ReadAsync()
    {
        if (setup != null && DateTimeOffset.UtcNow - readOn < Fresh) return setup;
        await reading.WaitAsync();
        try
        {
            if (setup != null && DateTimeOffset.UtcNow - readOn < Fresh) return setup;
            if (ReadOverride != null) setup = await ReadOverride();
            else
            {
                using var scope = scopes.CreateScope();
                setup = await MirrorSetupHosts.ReadAsync(scope.ServiceProvider.GetRequiredService<BitweenDbContext>());
            }
            readOn = DateTimeOffset.UtcNow;
            // Tests take new arrivals: what they answer with is read again too.
            answers.Clear();
            arrivals.Clear();
            return setup;
        }
        finally
        {
            reading.Release();
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

    /// <summary>Notes a call answered, or failed, while a test runs an exchange: the run shows what it called.</summary>
    void Record(MirrorRequest request, MirrorAnswer answer)
    {
        if (replaying is not { } r) return;
        r.Calls.Enqueue(new MirrorCall(DateTimeOffset.UtcNow, request.Method, request.Url.Host, request.Url.AbsolutePath, answer.Status, answer.Why,
            request.From, Exchange: Making(r, request.Url.Host, setup ?? MirrorSetupHosts.None)?.Id));
    }
}
