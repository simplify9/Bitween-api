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

    async Task<HttpResponseMessage> SendAsync(string path)
    {
        if (Disabled)
            throw new SWValidationException("REGISTRY_OFF", "The marketplace is turned off under Settings, Marketplace.");
        if (!Configured)
            throw new SWValidationException("REGISTRY_OFF", "No registry address is set under Settings, Marketplace.");

        HttpResponseMessage response;
        try
        {
            response = await Http.GetAsync(BaseUrl + path, HttpCompletionOption.ResponseHeadersRead);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            throw new SWValidationException("REGISTRY_UNREACHABLE", $"The registry at {BaseUrl} can't be reached: {ex.Message}");
        }

        if (response.IsSuccessStatusCode) return response;

        var status = response.StatusCode;
        response.Dispose();
        if (status == HttpStatusCode.NotFound)
            throw new SWNotFoundException(path);
        throw new SWValidationException("REGISTRY_UNREACHABLE", $"The registry at {BaseUrl} answered {(int)status}.");
    }
}
