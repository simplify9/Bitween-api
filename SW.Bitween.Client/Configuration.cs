using SW.Bitween.Model;

namespace SW.Bitween.Client;

/// <summary>
/// A list's search: names containing <see cref="NameContains"/>, matched as the database matches it
/// (case-sensitively on PostgreSQL), and any raw
/// <c>Field:Rule:Value</c> filters besides. What is left unset matches any.
/// </summary>
public class ListQuery
{
    public string? NameContains { get; set; }

    /// <summary>
    /// Raw filters, as the API takes them, such as <c>Inactive:1:false</c>. Rule 1 is equals, 4 contains.
    /// </summary>
    public IReadOnlyList<string>? Filters { get; set; }

    internal virtual List<string> ToFilters()
    {
        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(NameContains)) filters.Add($"Name:4:{NameContains.Trim()}");
        if (Filters != null) filters.AddRange(Filters);
        return filters;
    }
}

/// <summary>Which subscriptions: <see cref="ListQuery"/>'s, and of a type, information type or partner.</summary>
public class SubscriptionQuery : ListQuery
{
    public SubscriptionType? Type { get; set; }

    /// <summary>Of this information type (document, in the API).</summary>
    public int? InformationTypeId { get; set; }

    public int? PartnerId { get; set; }

    internal override List<string> ToFilters()
    {
        var filters = base.ToFilters();
        if (Type != null) filters.Add($"Type:1:{(int)Type}");
        if (InformationTypeId != null) filters.Add($"DocumentId:1:{InformationTypeId}");
        if (PartnerId != null) filters.Add($"PartnerId:1:{PartnerId}");
        return filters;
    }
}

/// <summary>A partner as its page reads it, and when each of its keys was last let through.</summary>
public class PartnerDetail : PartnerUpdate
{
    /// <summary>By key name. A key missing here hasn't had a call through since this was recorded.</summary>
    public Dictionary<string, DateTime> KeysLastUsedOn { get; set; } = [];
}

/// <summary>
/// A Bitween's partners. Settings named in <see cref="PartnerCreate.SecretProperties"/> are stored as
/// secrets: they come back as <c>__private__</c>, and sending that back keeps the stored value, so a
/// partner read with <see cref="GetAsync"/> can be changed and saved with <see cref="UpdateAsync"/>.
/// Keys come back masked, and a masked key sent back keeps the key it stands for.
/// </summary>
public sealed class Partners
{
    readonly BitweenClient bitween;

    internal Partners(BitweenClient bitween) => this.bitween = bitween;

    /// <summary>A page of partners, by name. Their settings' names come back, never their values.</summary>
    public Task<Page<PartnerRow>> SearchAsync(ListQuery? query = null, int page = 0, int size = 25,
        CancellationToken cancellation = default) =>
        bitween.SearchAsync<PartnerRow>("api/partners", (query ?? new ListQuery()).ToFilters(), page, size, cancellation);

    /// <summary>One partner, or null when there is none by that id.</summary>
    public Task<PartnerDetail?> GetAsync(int id, CancellationToken cancellation = default) =>
        bitween.GetOrNullAsync<PartnerDetail>($"api/partners/{id}", cancellation);

    /// <summary>Creates a partner, its settings included, and returns its id.</summary>
    public Task<int> CreateAsync(PartnerCreate partner, CancellationToken cancellation = default) =>
        bitween.PostAsync<int>("api/partners", partner, cancellation);

    /// <summary>
    /// Replaces the partner's name, settings, login identity and keys with <paramref name="partner"/>'s.
    /// A key left out is removed. Each key is <c>{ Key = its name, Value = the key }</c>.
    /// </summary>
    public Task UpdateAsync(int id, PartnerUpdate partner, CancellationToken cancellation = default) =>
        bitween.PostAsync($"api/partners/{id}", partner, cancellation);

    /// <summary>Deletes the partner.</summary>
    public Task DeleteAsync(int id, CancellationToken cancellation = default) =>
        bitween.DeleteAsync($"api/partners/{id}", cancellation);

    /// <summary>A new random key to give a partner. Nothing is stored until it is saved on one.</summary>
    public async Task<string> NewKeyAsync(CancellationToken cancellation = default) =>
        await bitween.GetTextAsync("api/partners/generatekey", cancellation);
}

/// <summary>
/// A Bitween's subscriptions. Every subscription is created inactive unless
/// <see cref="SubscriptionCreate.Inactive"/> is false. Adapter settings the adapter marks private come
/// back as <c>__private__</c>, and sending that back keeps the stored value, so a subscription read with
/// <see cref="GetAsync"/> can be changed and saved with <see cref="UpdateAsync"/>.
/// </summary>
public sealed class Subscriptions
{
    readonly BitweenClient bitween;

    internal Subscriptions(BitweenClient bitween) => this.bitween = bitween;

    /// <summary>A page of subscriptions. Their adapter settings come back empty: read one to see them.</summary>
    public Task<Page<SubscriptionSearch>> SearchAsync(SubscriptionQuery? query = null, int page = 0, int size = 25,
        CancellationToken cancellation = default) =>
        bitween.SearchAsync<SubscriptionSearch>("api/subscriptions", (query ?? new SubscriptionQuery()).ToFilters(), page,
            size, cancellation);

    /// <summary>One subscription, or null when there is none by that id.</summary>
    public Task<SubscriptionGet?> GetAsync(int id, CancellationToken cancellation = default) =>
        bitween.GetOrNullAsync<SubscriptionGet>($"api/subscriptions/{id}", cancellation);

    /// <summary>
    /// Creates a subscription, complete, and returns its id: either it exists as asked for, or nothing
    /// was made. A named adapter missing a setting it requires is refused.
    /// </summary>
    public Task<int> CreateAsync(SubscriptionCreate subscription, CancellationToken cancellation = default) =>
        bitween.PostAsync<int>("api/subscriptions", subscription, cancellation);

    /// <summary>
    /// Replaces the whole configuration with <paramref name="subscription"/>'s, except
    /// <see cref="SubscriptionConfiguration.Notifications"/>, which null leaves as they are.
    /// </summary>
    public Task UpdateAsync(int id, SubscriptionUpdate subscription, CancellationToken cancellation = default) =>
        bitween.PostAsync($"api/subscriptions/{id}", subscription, cancellation);

    /// <summary>Deletes the subscription. Refused while a gateway, route, response link or aggregation uses it.</summary>
    public Task DeleteAsync(int id, CancellationToken cancellation = default) =>
        bitween.DeleteAsync($"api/subscriptions/{id}", cancellation);

    /// <summary>Switches the subscription on, as the page's Enable does: false when it was on already.</summary>
    public Task<bool> ActivateAsync(int id, CancellationToken cancellation = default) => SetInactiveAsync(id, false, cancellation);

    /// <summary>Switches the subscription off: false when it was off already.</summary>
    public Task<bool> DeactivateAsync(int id, CancellationToken cancellation = default) => SetInactiveAsync(id, true, cancellation);

    async Task<bool> SetInactiveAsync(int id, bool inactive, CancellationToken cancellation)
    {
        var subscription = await GetAsync(id, cancellation) ?? throw NotFound(id);
        if (subscription.Inactive == inactive) return false;
        subscription.Inactive = inactive;
        await UpdateAsync(id, subscription, cancellation);
        return true;
    }

    /// <summary>
    /// Pauses the subscription: what arrives for it waits rather than runs. False when it was paused
    /// already. The API toggles, so it's read first; two callers at once can still undo each other.
    /// </summary>
    public Task<bool> PauseAsync(int id, CancellationToken cancellation = default) => SetPausedAsync(id, true, cancellation);

    /// <summary>Resumes a paused subscription: false when it wasn't paused.</summary>
    public Task<bool> ResumeAsync(int id, CancellationToken cancellation = default) => SetPausedAsync(id, false, cancellation);

    async Task<bool> SetPausedAsync(int id, bool paused, CancellationToken cancellation)
    {
        var subscription = await GetAsync(id, cancellation) ?? throw NotFound(id);
        if ((subscription.PausedOn != null) == paused) return false;
        await bitween.PostAsync($"api/subscriptions/{id}/pause", new SubscriptionPause(), cancellation);
        return true;
    }

    static BitweenClientException NotFound(int id) =>
        new($"Bitween has no subscription {id}.", System.Net.HttpStatusCode.NotFound);
}

/// <summary>A Bitween's information types (documents, in the API): read only.</summary>
public sealed class InformationTypes
{
    readonly BitweenClient bitween;

    internal InformationTypes(BitweenClient bitween) => this.bitween = bitween;

    public Task<Page<DocumentRow>> SearchAsync(ListQuery? query = null, int page = 0, int size = 25,
        CancellationToken cancellation = default) =>
        bitween.SearchAsync<DocumentRow>("api/documents", (query ?? new ListQuery()).ToFilters(), page, size, cancellation);

    /// <summary>One information type, or null when there is none by that id.</summary>
    public Task<DocumentRow?> GetAsync(int id, CancellationToken cancellation = default) =>
        bitween.GetOrNullAsync<DocumentRow>($"api/documents/{id}", cancellation);
}

/// <summary>
/// A Bitween's API gateways. A partner attached to a gateway runs one subscription through it, so
/// one gateway can send each partner's calls to an integration of its own.
/// </summary>
public sealed class ApiGateways
{
    readonly BitweenClient bitween;

    internal ApiGateways(BitweenClient bitween) => this.bitween = bitween;

    public Task<Page<ApiGatewayRow>> SearchAsync(ListQuery? query = null, int page = 0, int size = 25,
        CancellationToken cancellation = default) =>
        bitween.SearchAsync<ApiGatewayRow>("api/apigateways", (query ?? new ListQuery()).ToFilters(), page, size, cancellation);

    /// <summary>One gateway with its attached partners, or null when there is none by that id.</summary>
    public Task<ApiGatewayRow?> GetAsync(int id, CancellationToken cancellation = default) =>
        bitween.GetOrNullAsync<ApiGatewayRow>($"api/apigateways/{id}", cancellation);

    public Task<int> CreateAsync(ApiGatewayCreate gateway, CancellationToken cancellation = default) =>
        bitween.PostAsync<int>("api/apigateways", gateway, cancellation);

    /// <summary>Saves the gateway's own settings. Its partners are attached and removed on their own.</summary>
    public Task UpdateAsync(int id, ApiGatewayUpdate gateway, CancellationToken cancellation = default) =>
        bitween.PostAsync($"api/apigateways/{id}", gateway, cancellation);

    public Task DeleteAsync(int id, CancellationToken cancellation = default) =>
        bitween.DeleteAsync($"api/apigateways/{id}", cancellation);

    /// <summary>
    /// Attaches a partner, to run <see cref="ApiGatewayPartnerCreate.SubscriptionId"/> - a
    /// <see cref="SubscriptionType.GatewayApiCall"/> one - or a new integration defined inline.
    /// </summary>
    public Task AttachPartnerAsync(int id, ApiGatewayPartnerCreate attachment, CancellationToken cancellation = default) =>
        bitween.PostAsync($"api/apigateways/{id}/addpartner", attachment, cancellation);

    /// <summary>Points an attached partner at another subscription.</summary>
    public Task UpdatePartnerAsync(int id, int partnerId, int subscriptionId, CancellationToken cancellation = default) =>
        bitween.PostAsync($"api/apigateways/{id}/updatepartner",
            new ApiGatewayPartnerCreate { PartnerId = partnerId, SubscriptionId = subscriptionId }, cancellation);

    public Task DetachPartnerAsync(int id, int partnerId, CancellationToken cancellation = default) =>
        bitween.PostAsync($"api/apigateways/{id}/removepartner", new PartnerRef(partnerId), cancellation);

    sealed record PartnerRef(int PartnerId);
}

/// <summary>
/// A Bitween's bus gateways. Each route sends the gateway's messages that match its expression to a
/// subscription, as a partner, so one message type can reach a different integration per partner.
/// </summary>
public sealed class BusGateways
{
    readonly BitweenClient bitween;

    internal BusGateways(BitweenClient bitween) => this.bitween = bitween;

    public Task<Page<BusGatewayRow>> SearchAsync(ListQuery? query = null, int page = 0, int size = 25,
        CancellationToken cancellation = default) =>
        bitween.SearchAsync<BusGatewayRow>("api/busgateways", (query ?? new ListQuery()).ToFilters(), page, size, cancellation);

    /// <summary>One gateway with its routes, or null when there is none by that id.</summary>
    public Task<BusGatewayRow?> GetAsync(int id, CancellationToken cancellation = default) =>
        bitween.GetOrNullAsync<BusGatewayRow>($"api/busgateways/{id}", cancellation);

    public Task<int> CreateAsync(BusGatewayCreate gateway, CancellationToken cancellation = default) =>
        bitween.PostAsync<int>("api/busgateways", gateway, cancellation);

    /// <summary>Saves the gateway's own settings. Its routes are added and removed on their own.</summary>
    public Task UpdateAsync(int id, BusGatewayUpdate gateway, CancellationToken cancellation = default) =>
        bitween.PostAsync($"api/busgateways/{id}", gateway, cancellation);

    public Task DeleteAsync(int id, CancellationToken cancellation = default) =>
        bitween.DeleteAsync($"api/busgateways/{id}", cancellation);

    /// <summary>Adds a route and returns its id.</summary>
    public Task<int> AddRouteAsync(int id, BusGatewayRouteCreate route, CancellationToken cancellation = default) =>
        bitween.PostAsync<int>($"api/busgateways/{id}/addroute", route, cancellation);

    /// <summary>Replaces route <see cref="BusGatewayRouteUpdate.RouteId"/>'s subscription, partner and expression.</summary>
    public Task UpdateRouteAsync(int id, BusGatewayRouteUpdate route, CancellationToken cancellation = default) =>
        bitween.PostAsync($"api/busgateways/{id}/updateroute", route, cancellation);

    public Task RemoveRouteAsync(int id, int routeId, CancellationToken cancellation = default) =>
        bitween.PostAsync($"api/busgateways/{id}/removeroute", new RemoveRouteRequest { RouteId = routeId }, cancellation);
}

/// <summary>
/// A Bitween's notification channels: an adapter and its settings that a subscription's
/// <see cref="SubscriptionConfiguration.Notifications"/> send through. Secret settings come back as
/// <c>__private__</c>, and sending that back keeps the stored value while the adapter and where it
/// connects stay the same.
/// </summary>
public sealed class NotificationChannels
{
    readonly BitweenClient bitween;

    internal NotificationChannels(BitweenClient bitween) => this.bitween = bitween;

    public Task<Page<NotificationChannelRow>> SearchAsync(ListQuery? query = null, int page = 0, int size = 25,
        CancellationToken cancellation = default) =>
        bitween.SearchAsync<NotificationChannelRow>("api/notificationchannels", (query ?? new ListQuery()).ToFilters(), page,
            size, cancellation);

    /// <summary>One channel and what uses it, or null when there is none by that id.</summary>
    public Task<NotificationChannelGet?> GetAsync(int id, CancellationToken cancellation = default) =>
        bitween.GetOrNullAsync<NotificationChannelGet>($"api/notificationchannels/{id}", cancellation);

    public Task<int> CreateAsync(NotificationChannelCreate channel, CancellationToken cancellation = default) =>
        bitween.PostAsync<int>("api/notificationchannels", channel, cancellation);

    public Task UpdateAsync(int id, NotificationChannelUpdate channel, CancellationToken cancellation = default) =>
        bitween.PostAsync($"api/notificationchannels/{id}", channel, cancellation);

    /// <summary>Deletes the channel. Refused while anything still uses it.</summary>
    public Task DeleteAsync(int id, CancellationToken cancellation = default) =>
        bitween.DeleteAsync($"api/notificationchannels/{id}", cancellation);

    /// <summary>Sends one made-up notification through the channel as saved, and says whether it went.</summary>
    public Task<NotificationChannelTestResult> TestAsync(int id, CancellationToken cancellation = default) =>
        bitween.PostAsync<NotificationChannelTestResult>($"api/notificationchannels/{id}/test", new NotificationChannelTest(),
            cancellation);
}
