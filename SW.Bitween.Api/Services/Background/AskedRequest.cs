using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace SW.Bitween.Services.Background;

/// <summary>
/// What a request asked, copied off it, so it can be answered after the request that brought it has
/// ended and its context has been handed to the next caller.
/// </summary>
/// <remarks>
/// Taken once routing, authentication and authorization have run, so a new context made from it
/// carries their outcome - the endpoint, the user, the markers the endpoint middleware checks for -
/// and only what follows them runs again. That is everything from the request context onwards: the
/// handler, model binding and serialization, the same as for any request.
/// </remarks>
internal sealed class AskedRequest
{
    string method, scheme, protocol, traceId;
    HostString host;
    PathString pathBase, path;
    QueryString query;
    HeaderDictionary headers;
    ClaimsPrincipal user;
    (object Key, object Value)[] markers;
    Endpoint endpoint;
    RouteValueDictionary routeValues;
    IAuthenticateResultFeature authenticated;
    IPAddress remoteIp, localIp;
    int remotePort, localPort;
    byte[] sent;

    /// <summary>The largest body a request carried on in the background may send: a selection, not a file.</summary>
    public const int MaxBody = 4 * 1024 * 1024;

    /// <summary>
    /// What <paramref name="context"/> asked, its body read whole; null for a body too big to carry,
    /// which is then asked the usual way.
    /// </summary>
    public static async Task<AskedRequest> OfAsync(HttpContext context)
    {
        var request = context.Request;
        byte[] sent = [];
        if (request.ContentLength is > 0 || request.Headers.TransferEncoding.Count > 0)
        {
            if (request.ContentLength is > MaxBody) return null;
            // Rewound after, so a body too big to carry can still be read by the request as usual.
            request.EnableBuffering();
            using var copy = new MemoryStream();
            await request.Body.CopyToAsync(copy, context.RequestAborted);
            request.Body.Position = 0;
            if (copy.Length > MaxBody) return null;
            sent = copy.ToArray();
        }

        return new AskedRequest
        {
            sent = sent,
            method = request.Method,
            scheme = request.Scheme,
            protocol = request.Protocol,
            traceId = context.TraceIdentifier,
            host = request.Host,
            pathBase = request.PathBase,
            path = request.Path,
            query = request.QueryString,
            headers = new HeaderDictionary(request.Headers.ToDictionary(h => h.Key, h => h.Value)),
            user = context.User,
            // Only the "this middleware ran" markers: the endpoint middleware refuses an endpoint that
            // asks for authorization when the authorization middleware has not marked the request. The
            // rest of Items belongs to the request it was set on.
            markers = context.Items
                .Where(i => i.Key is string key && key.StartsWith("__") && key.EndsWith("MiddlewareWithEndpointInvoked"))
                .Select(i => (i.Key, i.Value))
                .ToArray(),
            endpoint = context.GetEndpoint(),
            routeValues = new RouteValueDictionary(request.RouteValues),
            authenticated = context.Features.Get<IAuthenticateResultFeature>(),
            remoteIp = context.Connection.RemoteIpAddress,
            localIp = context.Connection.LocalIpAddress,
            remotePort = context.Connection.RemotePort,
            localPort = context.Connection.LocalPort,
        };
    }

    /// <summary>The path and query, as asked.</summary>
    public string PathAndQuery => (pathBase + path + query).ToString();

    /// <summary>Whether it only reads, and so can be asked again from the start.</summary>
    public bool IsRead => HttpMethods.IsGet(method);

    /// <summary>What it asked, as the list of background requests says it: the path, after the method unless a read.</summary>
    public string Described => IsRead ? PathAndQuery : $"{method} {PathAndQuery}";

    /// <summary>
    /// A new context asking the same thing, writing its answer to <paramref name="body"/>, and told it
    /// was aborted when <paramref name="aborted"/> is cancelled.
    /// </summary>
    public DefaultHttpContext NewContext(System.IServiceProvider services, Stream body, CancellationToken aborted, int attempt)
    {
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature
        {
            Method = method,
            Scheme = scheme,
            Protocol = protocol,
            PathBase = pathBase.Value ?? "",
            Path = path.Value ?? "",
            QueryString = query.Value ?? "",
            RawTarget = PathAndQuery,
            Headers = new HeaderDictionary(headers.ToDictionary(h => h.Key, h => h.Value)),
            Body = new MemoryStream(sent, writable: false),
        });
        features.Set<IHttpResponseFeature>(new HttpResponseFeature());
        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(body));
        features.Set<IHttpRequestLifetimeFeature>(new HttpRequestLifetimeFeature { RequestAborted = aborted });
        features.Set<IHttpConnectionFeature>(new HttpConnectionFeature
        {
            RemoteIpAddress = remoteIp,
            LocalIpAddress = localIp,
            RemotePort = remotePort,
            LocalPort = localPort,
        });
        if (authenticated != null) features.Set(authenticated);

        var context = new DefaultHttpContext(features)
        {
            User = user,
            RequestServices = services,
            TraceIdentifier = $"{traceId}:{attempt}",
        };
        context.Request.Host = host;
        foreach (var (key, value) in markers) context.Items[key] = value;
        context.SetEndpoint(endpoint);
        context.Request.RouteValues = new RouteValueDictionary(routeValues);
        return context;
    }
}
