using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SW.Bitween.Domain.Connections;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Connections;

/// <summary>Turning a connection into what the API shows, and an edit into a connection.</summary>
internal static class ConnectionRows
{
    public const string Private = "__private__";
    static readonly Regex NamePattern = new("^[A-Za-z0-9_-]{1,100}$", RegexOptions.Compiled);

    public static ConnectionRow Row(Connection c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Grant = c.Grant.ToString(),
        AuthorizeUrl = c.AuthorizeUrl,
        TokenUrl = c.TokenUrl,
        ClientId = c.ClientId,
        ClientSecret = string.IsNullOrEmpty(c.ClientSecret) ? null : Private,
        Scopes = c.Scopes,
        AuthorizeParameters = c.AuthorizeParameters,
        TokenAuth = c.TokenAuth,
        Username = c.Username,
        Password = string.IsNullOrEmpty(c.Password) ? null : Private,
        Status = c.Status.ToString(),
        LastError = c.LastError,
        AccessTokenExpiresOn = c.AccessTokenExpiresOn,
        HasRefreshToken = !string.IsNullOrEmpty(c.RefreshToken),
        AuthorizedBy = c.AuthorizedBy,
        AuthorizedOn = c.AuthorizedOn,
        Reference = $"{{{{connections.{c.Name}.accessToken}}}}",
    };

    /// <summary>
    /// Applies an edit. Changing who the client is, or where its tokens come from, forgets the
    /// tokens: they were the old client's, and using them for the new one would be wrong either way.
    /// </summary>
    public static void Apply(Connection c, ConnectionSave save)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(save.Name) || !NamePattern.IsMatch(save.Name.Trim()))
            problems.Add("A name is letters, digits, '_' and '-', up to 100 of them; settings write it as {{connections.NAME.accessToken}}.");
        if (!Enum.TryParse<ConnectionGrant>(save.Grant, true, out var grant) || !Enum.IsDefined(grant))
            problems.Add("Grant is ClientCredentials, AuthorizationCode or Password.");
        if (!IsHttps(save.TokenUrl)) problems.Add("The token URL must be an https address (http only on this machine, for testing).");
        if (grant == ConnectionGrant.AuthorizationCode && !IsHttps(save.AuthorizeUrl))
            problems.Add("An authorization code connection needs the provider's authorize URL, an https address (http only on this machine, for testing).");
        if (string.IsNullOrWhiteSpace(save.ClientId)) problems.Add("The client ID is required.");
        if (grant == ConnectionGrant.Password && string.IsNullOrWhiteSpace(save.Username)) problems.Add("A password connection needs the username.");
        var tokenAuth = string.IsNullOrWhiteSpace(save.TokenAuth) ? "post" : save.TokenAuth.Trim().ToLowerInvariant();
        if (tokenAuth is not ("post" or "basic")) problems.Add("Token authentication is post or basic.");
        if (problems.Count > 0) throw new SWValidationException("CONNECTION_INVALID", string.Join(" ", problems));

        var clientChanged = c.Id != 0 && (c.Grant != grant || !Same(c.ClientId, save.ClientId) || !Same(c.TokenUrl, save.TokenUrl)
                                          || !Same(c.AuthorizeUrl, save.AuthorizeUrl) || !Same(c.Scopes, save.Scopes)
                                          || !Same(c.Username, save.Username));

        c.Name = save.Name.Trim();
        c.Grant = grant;
        c.AuthorizeUrl = Trimmed(save.AuthorizeUrl);
        c.TokenUrl = Trimmed(save.TokenUrl);
        c.ClientId = Trimmed(save.ClientId);
        c.Scopes = Trimmed(save.Scopes);
        c.AuthorizeParameters = Trimmed(save.AuthorizeParameters);
        c.TokenAuth = tokenAuth;
        c.Username = Trimmed(save.Username);
        if (save.ClientSecret is not null and not Private)
        {
            clientChanged |= c.Id != 0 && save.ClientSecret != c.ClientSecret;
            c.ClientSecret = save.ClientSecret.Length == 0 ? null : save.ClientSecret;
        }
        if (save.Password is not null and not Private)
        {
            clientChanged |= c.Id != 0 && save.Password != c.Password;
            c.Password = save.Password.Length == 0 ? null : save.Password;
        }

        if (c.Id == 0)
            c.Status = grant == ConnectionGrant.AuthorizationCode ? ConnectionStatus.NeedsAuthorizing : ConnectionStatus.New;
        else if (clientChanged)
            c.ForgetTokens();
    }

    /// <summary>
    /// https, or http to this machine itself: a provider played locally, to try a kit, has no
    /// certificate, and nothing sent to the loopback address leaves the machine.
    /// </summary>
    static bool IsHttps(string url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    static bool Same(string a, string b) => string.Equals(Trimmed(a), Trimmed(b), StringComparison.Ordinal);

    static string Trimmed(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Where a connection is referred to, by name, as a sentence; empty when nowhere.</summary>
    public static IEnumerable<string> UsesOf(string name, BitweenDbContext db)
    {
        var lowered = name.ToLowerInvariant();
        bool Uses(IEnumerable<IReadOnlyDictionary<string, string>> bags) =>
            bags.Any(b => b != null && Services.Connections.ConnectionTokens.NamesIn(b.Values).Contains(lowered));

        foreach (var s in db.Set<Domain.Subscription>().AsEnumerable())
            if (Uses([s.HandlerProperties, s.MapperProperties, s.ReceiverProperties, s.ValidatorProperties]))
                yield return $"subscription '{s.Name}'";
        foreach (var n in db.Set<Domain.Notifier>().AsEnumerable())
            if (Uses([n.HandlerProperties])) yield return $"notifier '{n.Name}'";
        foreach (var p in db.Set<Domain.RetryPolicy>().AsEnumerable())
            if (Uses([p.AlertHandlerProperties])) yield return $"retry policy '{p.Name}'";
        foreach (var p in db.Set<Domain.Partner>().AsEnumerable())
            if (Uses([p.AdapterProperties])) yield return $"partner '{p.Name}'";
        foreach (var g in db.Set<Domain.GlobalAdapterValuesSet>().AsEnumerable())
            if (Uses([g.Values])) yield return $"global values '{g.Id}'";
    }
}
