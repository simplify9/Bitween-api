using System.Text;

namespace SW.Bitween.NativeAdapters.ChannelMessages;

/// <summary>Posts a rendered message to a chat service's webhook, turning a refusal into an error that says why.</summary>
internal static class Webhook
{
    /// <summary>
    /// Long enough for a slow service, short enough that a dead one does not hold up the queue the
    /// notification is sent from.
    /// </summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static async Task Post(IHttpClientFactory httpClientFactory, DestinationCircuits circuits, string? url,
        string json, string service)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"The {service} webhook address has to be a full https:// address.");

        // The adapters' own client, so the private-network guard covers a webhook as it covers the
        // HTTP adapter.
        using var client = httpClientFactory.CreateClient(DynamicHttpProxy.ClientName);
        client.Timeout = Timeout;

        // Keyed as the HTTP adapter keys it, so both learn from each other's calls to the service.
        using var pass = circuits.Enter($"{uri.Scheme}://{uri.Authority}");
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(uri, new StringContent(json, Encoding.UTF8, "application/json"));
        }
        catch (Exception e)
        {
            pass.Failed(e);
            throw;
        }

        using (response)
        {
            if (DestinationCircuits.IsUnwell(response.StatusCode)) pass.Failed();
            else pass.Succeeded();

            if (response.IsSuccessStatusCode) return;

            // The service's own words are the useful part: Slack answers "invalid_token" or
            // "channel_not_found", Teams names the workflow that refused.
            var reply = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"{service} refused the message ({(int)response.StatusCode} {response.ReasonPhrase}). " +
                ChannelMessage.Shorten(reply, 500));
        }
    }
}
