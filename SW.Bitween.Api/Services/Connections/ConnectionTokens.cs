using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain.Connections;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services.Connections;

/// <summary>A connection that can't give a token now: not authorized, refused by the provider, or unreachable.</summary>
public class ConnectionException(string message) : BitweenException(message);

/// <summary>
/// The access tokens of <see cref="Connection"/>s: asked for, renewed before they expire, rotated
/// when the provider rotates them, and put into adapter settings that refer to them.
/// </summary>
/// <remarks>
/// <para>
/// Works in a scope of its own, so renewing a token never saves whatever the caller - an exchange
/// half way through - has pending in its own context.
/// </para>
/// <para>
/// Token writes go through <c>ExecuteUpdate</c>, past the change tracker, so a renewal every hour
/// isn't an audit entry; adding, editing and authorizing a connection are.
/// </para>
/// </remarks>
public class ConnectionTokens(IServiceScopeFactory scopes, SealedState sealedState, IHttpContextAccessor httpContextAccessor,
    FileLinks fileLinks, ILogger<ConnectionTokens> logger)
{
    public const string CallbackPath = "/oauth-callback";
    public const string FlowCookie = "bitween_connection";
    const string FlowPurpose = "bitween:connection-flow:v1";
    static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(10);

    /// <summary>A token good for less than this is renewed first, so it can't expire while the adapter uses it.</summary>
    static readonly TimeSpan Margin = TimeSpan.FromSeconds(120);

    /// <summary>How long one node may hold the right to renew before another may take it over.</summary>
    static readonly TimeSpan RenewalLease = TimeSpan.FromSeconds(30);

    /// <summary><c>{{connections.NAME.FIELD}}</c>.</summary>
    static readonly Regex Reference = new(@"\{\{connections\.([A-Za-z0-9_-]+)\.([A-Za-z]+)\}\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>For tests: answers in place of the network, as <see cref="OidcSignIn"/>'s does.</summary>
    internal static Func<HttpMessageHandler> HandlerOverride { get; set; }

    static HttpClient caught;

    /// <summary>
    /// On a staging that mirrors prod, token requests go through <paramref name="handler"/>, which
    /// answers those for test addresses itself (see <see cref="Mirror.MirrorCatchingHandler"/>).
    /// </summary>
    public static void CatchWith(HttpMessageHandler handler) => caught = new HttpClient(handler) { Timeout = Http.Timeout };

    static HttpClient Client => HandlerOverride is { } handler ? new HttpClient(handler()) : caught ?? Http;

    /// <summary>Whether any of <paramref name="values"/> refers to a connection.</summary>
    public static bool Refers(IEnumerable<string> values) => values?.Any(v => v != null && Reference.IsMatch(v)) == true;

    /// <summary>The connection names <paramref name="values"/> refer to.</summary>
    public static IEnumerable<string> NamesIn(IEnumerable<string> values) =>
        (values ?? []).Where(v => v != null).SelectMany(v => Reference.Matches(v).Select(m => m.Groups[1].Value.ToLowerInvariant())).Distinct();

    /// <summary>
    /// <paramref name="properties"/> with every connection reference replaced by what it names: the
    /// same dictionary, untouched, when there are none, which is every adapter call but a few.
    /// </summary>
    public async Task<IDictionary<string, string>> ResolveAsync(IDictionary<string, string> properties, CancellationToken cancellation = default)
    {
        if (properties == null || !Refers(properties.Values)) return properties;

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in NamesIn(properties.Values))
            tokens[name] = await AccessTokenAsync(name, cancellation);

        var resolved = new Dictionary<string, string>(properties.Count);
        foreach (var (key, value) in properties)
            resolved[key] = value == null ? null : Reference.Replace(value, m => m.Groups[2].Value.ToLowerInvariant() switch
            {
                "accesstoken" => tokens[m.Groups[1].Value],
                "authorization" => "Bearer " + tokens[m.Groups[1].Value],
                _ => throw new ConnectionException(
                    $"'{m.Value}' names nothing a connection has: use {{{{connections.{m.Groups[1].Value}.accessToken}}}} or .authorization."),
            });
        return resolved;
    }

    /// <summary>A valid access token for the connection named <paramref name="name"/>, renewed first when it has to be.</summary>
    public async Task<string> AccessTokenAsync(string name, CancellationToken cancellation = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var lowered = name.ToLowerInvariant();

        // Waits out another node's renewal, for up to a few leases, rather than renewing beside it.
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var connection = await db.Set<Connection>().AsNoTracking().SingleOrDefaultAsync(c => c.Name.ToLower() == lowered, cancellation)
                             ?? throw new ConnectionException($"There is no connection named '{name}'. Add it on the Connections page.");
            var now = DateTime.UtcNow;
            if (connection.HasFreshToken(now, Margin)) return connection.AccessToken;

            if (await ClaimRenewalAsync(db, connection.Id, now, cancellation))
            {
                // Read again now the lease is ours: another node may have renewed between the read
                // above and the claim, and a provider that rotates refresh tokens refuses the one
                // read before that, which would leave the connection needing authorizing.
                connection = await db.Set<Connection>().AsNoTracking().SingleAsync(c => c.Id == connection.Id, cancellation);
                if (connection.HasFreshToken(DateTime.UtcNow, Margin))
                {
                    await db.Set<Connection>().Where(c => c.Id == connection.Id)
                        .ExecuteUpdateAsync(u => u.SetProperty(c => c.RenewingUntil, (DateTime?)null), cancellation);
                    return connection.AccessToken;
                }
                return await RenewAsync(db, connection, cancellation);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250 + attempt * 50), cancellation);
        }
        throw new ConnectionException($"Connection '{name}' is being renewed by another node, which hasn't finished. Try again.");
    }

    /// <summary>Renews now, whether or not the token is still good, and says how it went.</summary>
    public async Task<Connection> TestAsync(int id, CancellationToken cancellation = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var connection = await db.Set<Connection>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellation)
                         ?? throw new SWNotFoundException(id.ToString());
        if (!await ClaimRenewalAsync(db, id, DateTime.UtcNow, cancellation))
            throw new ConnectionException($"Connection '{connection.Name}' is being renewed right now. Try again in a moment.");
        try
        {
            await RenewAsync(db, connection, cancellation);
        }
        catch (ConnectionException)
        {
            // Recorded on the connection, which is what a test shows.
        }
        return await db.Set<Connection>().AsNoTracking().SingleAsync(c => c.Id == id, cancellation);
    }

    /// <summary>One conditional update, so of two nodes only one gets to renew.</summary>
    static async Task<bool> ClaimRenewalAsync(BitweenDbContext db, int id, DateTime now, CancellationToken cancellation) =>
        await db.Set<Connection>()
            .Where(c => c.Id == id && (c.RenewingUntil == null || c.RenewingUntil < now))
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.RenewingUntil, now + RenewalLease), cancellation) == 1;

    async Task<string> RenewAsync(BitweenDbContext db, Connection connection, CancellationToken cancellation)
    {
        Dictionary<string, string> form;
        switch (connection.Grant)
        {
            case ConnectionGrant.ClientCredentials:
                form = new() { ["grant_type"] = "client_credentials" };
                break;
            case ConnectionGrant.Password:
                form = new() { ["grant_type"] = "password", ["username"] = connection.Username ?? "", ["password"] = connection.Password ?? "" };
                break;
            case ConnectionGrant.AuthorizationCode when !string.IsNullOrEmpty(connection.RefreshToken):
                form = new() { ["grant_type"] = "refresh_token", ["refresh_token"] = connection.RefreshToken };
                break;
            default:
                await ReleaseAsync(db, connection.Id, ConnectionStatus.NeedsAuthorizing,
                    "Not authorized yet: open it on the Connections page and choose Authorize.", cancellation);
                throw new ConnectionException($"Connection '{connection.Name}' needs authorizing: open it on the Connections page and choose Authorize.");
        }
        // The scope a client credentials or password token is asked for; a refresh keeps what was approved.
        if (connection.Grant != ConnectionGrant.AuthorizationCode && !string.IsNullOrWhiteSpace(connection.Scopes))
            form["scope"] = connection.Scopes.Trim();

        TokenAnswer answer;
        try
        {
            answer = await RequestTokenAsync(connection, form, cancellation);
        }
        catch (TokenRefused ex)
        {
            // invalid_grant on a refresh: the provider no longer honours the refresh token, as when
            // someone revoked access or the token was rotated elsewhere. Only approving again helps.
            var revoked = connection.Grant == ConnectionGrant.AuthorizationCode && ex.Error == "invalid_grant";
            await ReleaseAsync(db, connection.Id, revoked ? ConnectionStatus.NeedsAuthorizing : ConnectionStatus.Error, ex.Message, cancellation,
                forgetRefreshToken: revoked);
            throw new ConnectionException(revoked
                ? $"Connection '{connection.Name}' needs authorizing again: the provider no longer accepts its refresh token ({ex.Message})."
                : $"Connection '{connection.Name}' couldn't get a token: {ex.Message}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellation.IsCancellationRequested)
        {
            await ReleaseAsync(db, connection.Id, ConnectionStatus.Error, $"The token endpoint couldn't be reached: {ex.Message}", cancellation);
            throw new ConnectionException($"Connection '{connection.Name}' couldn't reach its token endpoint: {ex.Message}");
        }

        await StoreAsync(db, connection.Id, answer, cancellation);
        return answer.AccessToken;
    }

    static Task ReleaseAsync(BitweenDbContext db, int id, ConnectionStatus status, string error, CancellationToken cancellation,
        bool forgetRefreshToken = false) =>
        db.Set<Connection>().Where(c => c.Id == id).ExecuteUpdateAsync(u => u
            .SetProperty(c => c.RenewingUntil, (DateTime?)null)
            .SetProperty(c => c.Status, status)
            .SetProperty(c => c.LastError, Shorten(error, 2000))
            .SetProperty(c => c.RefreshToken, c => forgetRefreshToken ? null : c.RefreshToken)
            .SetProperty(c => c.AccessToken, c => forgetRefreshToken ? null : c.AccessToken), cancellation);

    /// <summary>
    /// Saves a token answer. Encryption is the column converter's job; ExecuteUpdate applies it to
    /// the values it sets, as SaveChanges would.
    /// </summary>
    static Task StoreAsync(BitweenDbContext db, int id, TokenAnswer answer, CancellationToken cancellation)
    {
        var expires = DateTime.UtcNow.AddSeconds(answer.ExpiresIn is > 0 ? answer.ExpiresIn.Value : 3600);
        return answer.RefreshToken is { } rotated
            ? db.Set<Connection>().Where(c => c.Id == id).ExecuteUpdateAsync(u => u
                .SetProperty(c => c.AccessToken, answer.AccessToken)
                .SetProperty(c => c.AccessTokenExpiresOn, expires)
                .SetProperty(c => c.RefreshToken, rotated)
                .SetProperty(c => c.RenewingUntil, (DateTime?)null)
                .SetProperty(c => c.Status, ConnectionStatus.Connected)
                .SetProperty(c => c.LastError, (string)null), cancellation)
            : db.Set<Connection>().Where(c => c.Id == id).ExecuteUpdateAsync(u => u
                .SetProperty(c => c.AccessToken, answer.AccessToken)
                .SetProperty(c => c.AccessTokenExpiresOn, expires)
                .SetProperty(c => c.RenewingUntil, (DateTime?)null)
                .SetProperty(c => c.Status, ConnectionStatus.Connected)
                .SetProperty(c => c.LastError, (string)null), cancellation);
    }

    record TokenAnswer(string AccessToken, string RefreshToken, int? ExpiresIn);

    class TokenRefused(string message, string error) : Exception(message)
    {
        public string Error { get; } = error;
    }

    static async Task<TokenAnswer> RequestTokenAsync(Connection connection, Dictionary<string, string> form, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(connection.TokenUrl)) throw new TokenRefused("It has no token URL.", null);

        using var request = new HttpRequestMessage(HttpMethod.Post, connection.TokenUrl.Trim());
        if (string.Equals(connection.TokenAuth, "basic", StringComparison.OrdinalIgnoreCase))
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{Uri.EscapeDataString(connection.ClientId ?? "")}:{Uri.EscapeDataString(connection.ClientSecret ?? "")}")));
        else
        {
            form["client_id"] = connection.ClientId ?? "";
            if (!string.IsNullOrEmpty(connection.ClientSecret)) form["client_secret"] = connection.ClientSecret;
        }
        request.Headers.Accept.ParseAdd("application/json");
        request.Content = new FormUrlEncodedContent(form);

        using var response = await Client.SendAsync(request, cancellation);
        var body = await response.Content.ReadAsStringAsync(cancellation);
        JsonElement json = default;
        try
        {
            json = JsonDocument.Parse(body).RootElement;
        }
        catch (JsonException)
        {
            // Said below.
        }

        string Text(string property) =>
            json.ValueKind == JsonValueKind.Object && json.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        if (!response.IsSuccessStatusCode)
        {
            var error = Text("error");
            var description = Text("error_description");
            throw new TokenRefused($"the provider answered {(int)response.StatusCode}" +
                                   (error != null ? $": {error}{(description != null ? $", {description}" : "")}" : $": {Shorten(body, 300)}"), error);
        }

        var accessToken = Text("access_token") ?? throw new TokenRefused($"the provider's answer has no access_token: {Shorten(body, 300)}", null);
        int? expiresIn = json.TryGetProperty("expires_in", out var e)
            ? e.ValueKind == JsonValueKind.Number ? e.GetInt32() : int.TryParse(e.GetString(), out var parsed) ? parsed : null
            : null;
        return new TokenAnswer(accessToken, Text("refresh_token"), expiresIn);
    }

    record Flow(int ConnectionId, string State, string Verifier, string RedirectUri, long Expires);

    /// <summary>Where the provider sends the browser back after approval.</summary>
    public string RedirectUri => fileLinks.BaseUrl + CallbackPath;

    /// <summary>
    /// The provider's approval page for an authorization code connection, remembering the attempt
    /// in a cookie, with PKCE.
    /// </summary>
    public async Task<string> StartAuthorizationAsync(int id, CancellationToken cancellation = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var connection = await scope.ServiceProvider.GetRequiredService<BitweenDbContext>().Set<Connection>().AsNoTracking()
                             .SingleOrDefaultAsync(c => c.Id == id, cancellation)
                         ?? throw new SWNotFoundException(id.ToString());
        if (connection.Grant != ConnectionGrant.AuthorizationCode)
            throw new SWValidationException("CONNECTION_GRANT", "Only an authorization code connection is authorized; this one gets its tokens by itself.");
        if (string.IsNullOrWhiteSpace(connection.AuthorizeUrl))
            throw new SWValidationException("CONNECTION_AUTHORIZE_URL", "It needs the provider's authorize URL first.");

        var flow = new Flow(id, SealedState.Random(), SealedState.Random(48), RedirectUri,
            DateTimeOffset.UtcNow.Add(FlowLifetime).ToUnixTimeSeconds());
        httpContextAccessor.HttpContext?.Response.Cookies.Append(FlowCookie, sealedState.Seal(FlowPurpose, flow), new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, MaxAge = FlowLifetime, Path = "/",
        });

        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = connection.ClientId ?? "",
            ["redirect_uri"] = flow.RedirectUri,
            ["state"] = flow.State,
            ["code_challenge"] = SealedState.Challenge(flow.Verifier),
            ["code_challenge_method"] = "S256",
        };
        if (!string.IsNullOrWhiteSpace(connection.Scopes)) query["scope"] = connection.Scopes.Trim();
        foreach (var (key, value) in QueryHelpers.ParseQuery((connection.AuthorizeParameters ?? "").TrimStart('?')))
            query[key] = value.ToString();
        return QueryHelpers.AddQueryString(connection.AuthorizeUrl.Trim(), query);
    }

    /// <summary>Finishes an approval: the code traded for tokens, which are kept. Returns which connection it was.</summary>
    public async Task<(int Id, string Name)> CompleteAuthorizationAsync(string code, string state, string actor, CancellationToken cancellation = default)
    {
        var context = httpContextAccessor.HttpContext;
        var flow = sealedState.Open<Flow>(FlowPurpose, context?.Request.Cookies[FlowCookie]);
        context?.Response.Cookies.Delete(FlowCookie, new CookieOptions { Path = "/", Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax });

        if (flow == null || DateTimeOffset.UtcNow.ToUnixTimeSeconds() > flow.Expires)
            throw new SWValidationException("CONNECTION_FLOW", "The approval took too long or was started in another browser. Choose Authorize again.");
        if (string.IsNullOrEmpty(state) || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(flow.State)))
            throw new SWValidationException("CONNECTION_STATE", "The approval didn't come back from the request this browser made. Choose Authorize again.");

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var connection = await db.Set<Connection>().SingleOrDefaultAsync(c => c.Id == flow.ConnectionId, cancellation)
                         ?? throw new SWNotFoundException(flow.ConnectionId.ToString());

        TokenAnswer answer;
        try
        {
            answer = await RequestTokenAsync(connection, new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code ?? "",
                ["redirect_uri"] = flow.RedirectUri,
                ["code_verifier"] = flow.Verifier,
            }, cancellation);
        }
        catch (Exception ex) when (ex is TokenRefused or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Authorizing connection {Name} failed: {Reason}", connection.Name, ex.Message);
            connection.Status = ConnectionStatus.Error;
            connection.LastError = Shorten($"Authorizing failed: {ex.Message}", 2000);
            await db.SaveChangesAsync(cancellation);
            throw new SWValidationException("CONNECTION_AUTHORIZE", $"The provider didn't authorize the connection: {ex.Message}");
        }

        if (string.IsNullOrEmpty(answer.RefreshToken))
            logger.LogWarning("Connection {Name} was authorized without a refresh token; it will need authorizing again when the token expires.", connection.Name);

        // Through the change tracker, unlike a renewal: who authorized a connection is what the trail is for.
        connection.AccessToken = answer.AccessToken;
        connection.AccessTokenExpiresOn = DateTime.UtcNow.AddSeconds(answer.ExpiresIn is > 0 ? answer.ExpiresIn.Value : 3600);
        connection.RefreshToken = answer.RefreshToken;
        connection.Status = ConnectionStatus.Connected;
        connection.LastError = string.IsNullOrEmpty(answer.RefreshToken)
            ? "The provider gave no refresh token, so this will need authorizing again when the token expires. Some providers give one only when asked for offline access (a scope such as offline_access, or access_type=offline)."
            : null;
        connection.AuthorizedBy = actor;
        connection.AuthorizedOn = DateTime.UtcNow;
        connection.RenewingUntil = null;
        await db.SaveChangesAsync(cancellation);
        return (connection.Id, connection.Name);
    }

    static string Shorten(string text, int length) => text == null || text.Length <= length ? text : text[..length];
}
