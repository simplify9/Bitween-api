using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>
/// Whether Bitween's files open straight from the bucket, without credentials. Exchange files are written
/// private, but a bucket or container opened to everyone makes them readable anyway, and only the storage
/// service can say: so a small private file is written next to them and asked for anonymously. Held for a
/// few minutes, like the deletion rules.
/// </summary>
public class StorageAccess(IServiceProvider serviceProvider, BitweenOptions options, ILogger<StorageAccess> logger)
{
    public const string ProbeName = ".access-check";
    public const string ProbeText = "Bitween checks that this file can't be opened without credentials.";

    private static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(5);
    private static readonly HttpClient Anonymous = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool? _open;
    private DateTime _checkedOn = DateTime.MinValue;

    /// <summary>Why Bitween couldn't make its container private at startup, when it tried and failed.</summary>
    public string CouldNotMakePrivate { get; set; }

    /// <summary>
    /// <c>true</c> when the file opened without credentials, <c>false</c> when the storage refused it, and
    /// <c>null</c> when that can't be told — the storage address isn't reachable from here, say.
    /// </summary>
    public virtual async Task<bool?> IsOpenToAnyoneAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        if (!refresh && DateTime.UtcNow - _checkedOn < KeepFor) return _open;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && DateTime.UtcNow - _checkedOn < KeepFor) return _open;

            _open = await Probe(cancellationToken);
            _checkedOn = DateTime.UtcNow;
            return _open;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool?> Probe(CancellationToken cancellationToken)
    {
        string url;
        try
        {
            using var scope = serviceProvider.CreateScope();
            var cloudFiles = scope.ServiceProvider.GetRequiredService<ICloudFilesService>();
            var key = $"{options.DocumentPrefix}/{ProbeName}";
            await cloudFiles.WriteTextAsync(ProbeText, new WriteFileSettings { Key = key, ContentType = "text/plain" });
            url = cloudFiles.GetUrl(key);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not write the file that checks whether storage is open to everyone.");
            return null;
        }

        // A local folder in development: nothing to ask over HTTP.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;

        try
        {
            return await Ask(uri, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.SecureConnectionError &&
                                              uri.Scheme == Uri.UriSchemeHttps)
        {
            // The S3 library hands out https addresses even for a plain-http server, a MinIO of the client's
            // own say: when TLS can't start at all, the same address is asked over http.
            var http = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttp, Port = uri.Port }.Uri;
            try
            {
                return await Ask(http, cancellationToken);
            }
            catch (Exception retry) when (retry is HttpRequestException or TaskCanceledException)
            {
                logger.LogInformation(retry, "Could not reach {Url} to check whether storage is open to everyone.", http);
                return null;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogInformation(ex, "Could not reach {Url} to check whether storage is open to everyone.", url);
            return null;
        }
    }

    private static async Task<bool?> Ask(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await Anonymous.GetAsync(uri, cancellationToken);
        return Opens(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>
    /// Open means the file itself came back. Refused or not found means private; anything else — a server
    /// error, some other page — can't be told either way.
    /// </summary>
    public static bool? Opens(HttpStatusCode status, string body) =>
        status == HttpStatusCode.OK && body == ProbeText ? true
        : (int)status is >= 400 and < 500 ? false
        : null;
}
