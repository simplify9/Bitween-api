using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services.Marketplace;

public record RegistryCard(string Id, string DisplayName, string Summary, string Publisher, string PublisherName,
    string Tier, string Pricing, string Latest, string Runtime, string Kinds, string[] Categories,
    string IconDataUri, long Downloads, DateTime UpdatedOn);

public record RegistrySearch(List<RegistryCard> Result, int TotalCount);

public record RegistryVersionSummary(string Version, string Status, string Sha256, long Size, string Runtime,
    string Kinds, string MinBitweenVersion, DateTime SubmittedOn, DateTime? SignedOn, long Downloads,
    DateTime? WithdrawnOn, DateTime? RevokedOn, string RevokeReason);

public record RegistryPackage(RegistryCard Card, string Description, string Homepage, string Repository,
    string License, List<RegistryVersionSummary> Versions);

public record RegistryVersion(string PackageId, string Version, string Status, string Sha256, long Size,
    JsonElement Manifest, string Publisher, string Tier, string Pricing, string PublisherKeyId,
    string PublisherPublicKey, string PublisherSignature, string RegistryKeyId, string RegistrySignature,
    DateTime? SignedOn, string DownloadUrl);

public record RegistrySignedDocument(string Payload, string KeyId, string Signature);

public record RegistryRegistration(string InstanceId, string Token, string DisplayName, int CustomerId, string CustomerName);

public record RegistryUsageLine(string PackageId, string Version, int Subscriptions, long Calls, long Failures);

/// <summary>
/// The registry's read API, as the Marketplace tab and the installer use it. Nothing it returns is
/// trusted for installing until <see cref="RegistrySignatures"/> has checked it.
/// </summary>
public class RegistryClient(BitweenOptions options)
{
    // One client for the process, recycling its connections so a registry moved to a new address
    // is followed. Redirects are followed: downloads answer with one to the storage link.
    static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AllowAutoRedirect = true,
    })
    {
        Timeout = TimeSpan.FromMinutes(2),
    };

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>For tests: answers in place of the network, as the CLI's BitweenApi.HandlerOverride does.</summary>
    internal static Func<HttpMessageHandler> HandlerOverride { get; set; }

    static HttpClient Client => HandlerOverride is { } handler ? new HttpClient(handler()) : Http;

    /// <summary>The largest package downloaded, matching what the upload endpoint accepts.</summary>
    public const long MaxPackageBytes = 200L * 1024 * 1024;

    /// <summary>Turned off under Settings: nothing is fetched.</summary>
    public bool Disabled => !options.RegistryEnabled;

    public bool Configured => options.RegistryEnabled && !string.IsNullOrWhiteSpace(options.RegistryUrl);

    public string BaseUrl => options.RegistryUrl?.Trim().TrimEnd('/');

    public Task<RegistrySearch> SearchAsync(string q, string tier) =>
        GetAsync<RegistrySearch>($"/v1/search?size=100&q={Uri.EscapeDataString(q ?? "")}" +
                                 (string.IsNullOrWhiteSpace(tier) ? "" : $"&tier={Uri.EscapeDataString(tier)}"));

    public Task<RegistryPackage> PackageAsync(string id) =>
        GetAsync<RegistryPackage>($"/v1/packages/{Uri.EscapeDataString(id)}");

    public Task<RegistryVersion> VersionAsync(string id, string version) =>
        GetAsync<RegistryVersion>($"/v1/packages/{Uri.EscapeDataString(id)}/versions/{Uri.EscapeDataString(version)}");

    public Task<RegistrySignedDocument> RevocationsAsync() => GetAsync<RegistrySignedDocument>("/v1/revocations");

    /// <summary>Whether this Bitween is registered: it has a token to send.</summary>
    public bool Registered => !string.IsNullOrWhiteSpace(options.RegistryInstanceToken);

    /// <summary>A registration code becomes an instance id and a token. Sent without a token: there is none yet.</summary>
    public Task<RegistryRegistration> RegisterAsync(string code, string displayName, string bitweenVersion) =>
        PostAsync<RegistryRegistration>("/v1/instances/register", new { code, displayName, bitweenVersion }, withToken: false);

    /// <summary>The licences the registry holds for this instance, each signed; nothing is trusted until checked.</summary>
    public Task<List<RegistrySignedDocument>> EntitlementsAsync() => GetAsync<List<RegistrySignedDocument>>("/v1/instances/me/entitlements");

    public async Task ReportUsageAsync(DateOnly day, string bitweenVersion, IReadOnlyList<RegistryUsageLine> lines)
    {
        using var _ = await SendAsync("/v1/instances/me/usage", HttpMethod.Post, new { day = day.ToString("yyyy-MM-dd"), bitweenVersion, lines });
    }

    async Task<T> PostAsync<T>(string path, object body, bool withToken = true)
    {
        using var response = await SendAsync(path, HttpMethod.Post, body, withToken);
        return await response.Content.ReadFromJsonAsync<T>(Json)
               ?? throw new SWValidationException("REGISTRY_UNREACHABLE", "The registry sent an empty answer.");
    }

    public async Task<byte[]> DownloadAsync(string id, string version)
    {
        using var response = await SendAsync($"/v1/packages/{Uri.EscapeDataString(id)}/versions/{Uri.EscapeDataString(version)}/download");
        if (response.Content.Headers.ContentLength > MaxPackageBytes)
            throw new SWValidationException("REGISTRY_PACKAGE", "The package is larger than Bitween accepts.");
        return await response.Content.ReadAsByteArrayAsync();
    }

    async Task<T> GetAsync<T>(string path)
    {
        using var response = await SendAsync(path);
        return await response.Content.ReadFromJsonAsync<T>(Json)
               ?? throw new SWValidationException("REGISTRY_UNREACHABLE", "The registry sent an empty answer.");
    }

    async Task<HttpResponseMessage> SendAsync(string path, HttpMethod method = null, object body = null, bool withToken = true)
    {
        if (Disabled)
            throw new SWValidationException("REGISTRY_OFF", "The marketplace is turned off under Settings, Marketplace.");
        if (!Configured)
            throw new SWValidationException("REGISTRY_OFF", "No registry address is set under Settings, Marketplace.");

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(method ?? HttpMethod.Get, BaseUrl + path);
            // The instance token opens private listings and licences; a Bitween that never registered sends none.
            if (withToken && Registered)
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.RegistryInstanceToken.Trim());
            if (body != null) request.Content = JsonContent.Create(body, options: Json);
            response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            throw new SWValidationException("REGISTRY_UNREACHABLE", $"The registry at {BaseUrl} can't be reached: {ex.Message}");
        }

        if (response.IsSuccessStatusCode) return response;

        var status = response.StatusCode;
        string said = null;
        try
        {
            // The registry answers a refusal with { code, message }, which is the thing to show.
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
            if (problem.ValueKind == JsonValueKind.Object && problem.TryGetProperty("message", out var m)) said = m.GetString();
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or NotSupportedException)
        {
            // Not JSON: the status is what there is.
        }
        response.Dispose();
        if (status == HttpStatusCode.NotFound)
            throw new SWNotFoundException(path);
        if (status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.Conflict && said != null)
            throw new SWValidationException("REGISTRY_REFUSED", said);
        throw new SWValidationException("REGISTRY_UNREACHABLE", $"The registry at {BaseUrl} answered {(int)status}{(said == null ? "" : ": " + said)}.");
    }
}
