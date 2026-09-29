using System;
using System.Collections.Concurrent;
using System.Net.Http;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace SW.Bitween;

/// <summary>Where a JWT gateway gets the public keys of the login server it trusts.</summary>
public interface IGatewayIssuers
{
    BaseConfigurationManager For(string issuer);
}

/// <summary>
/// Reads each login server's keys from its discovery document, keeping one cache per server for
/// the life of the process: fetching them per call would put the login server in the path of
/// every partner request.
/// </summary>
public class OpenIdGatewayIssuers : IGatewayIssuers
{
    // Until a login server's keys have been read once, every call waits on reading them. The
    // default timeout is 100 seconds — a partner call held that long by a firewall dropping the
    // connection, rather than refused in a few.
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    readonly ConcurrentDictionary<string, BaseConfigurationManager> byIssuer = new();

    public BaseConfigurationManager For(string issuer) => byIssuer.GetOrAdd(issuer, _ =>
        new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{issuer.TrimEnd('/')}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(Http) { RequireHttps = !IsLoopback(issuer) })
        {
            // Left on, a key that once checked a token keeps being accepted for an hour after the
            // login server stops publishing it. A key dropped that suddenly is usually one that
            // leaked, and routine rotations keep the old key published until its tokens expire.
            UseLastKnownGoodConfiguration = false,
        });

    /// <summary>
    /// A full https address. Plain http only on this machine, for trying a gateway out against a
    /// login server running locally — anywhere else the keys could be swapped on the way.
    /// </summary>
    public static bool IsUsable(string issuer) =>
        Uri.TryCreate(issuer, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    static bool IsLoopback(string issuer) =>
        Uri.TryCreate(issuer, UriKind.Absolute, out var uri) && uri.IsLoopback;
}
