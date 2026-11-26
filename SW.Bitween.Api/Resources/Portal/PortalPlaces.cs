using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;

namespace SW.Bitween.Resources.Portal;

/// <summary>
/// Where a partner is connected, and so where they may have their own mapping: the subscriptions
/// that belong to them alone, the API gateways they are attached to, and the bus gateway routes that
/// name them. Each is one subscription, and their mapping of it is used for them alone.
/// </summary>
internal static class PortalPlaces
{
    public record Place(int SubscriptionId, string Via);

    public static async Task<List<Place>> ListAsync(BitweenDbContext db, int partnerId)
    {
        var own = await db.Set<Subscription>().AsNoTracking()
            .Where(s => s.PartnerId == partnerId)
            .Select(s => new Place(s.Id, "Your subscription")).ToListAsync();
        var gateways = await db.Set<ApiGatewayPartner>().AsNoTracking()
            .Where(g => g.PartnerId == partnerId)
            .Select(g => new Place(g.SubscriptionId, "API gateway " + g.ApiGateway.Name)).ToListAsync();
        var routes = await db.Set<BusGatewayRoute>().AsNoTracking()
            .Where(r => r.PartnerId == partnerId)
            .Select(r => new Place(r.SubscriptionId, "Bus gateway " + r.BusGateway.Name)).ToListAsync();

        // One subscription reached two ways is still one mapping.
        return own.Concat(gateways).Concat(routes)
            .GroupBy(p => p.SubscriptionId)
            .Select(g => new Place(g.Key, string.Join(", ", g.Select(p => p.Via).Distinct())))
            .ToList();
    }

    public static async Task<Place> FindAsync(BitweenDbContext db, int partnerId, int subscriptionId) =>
        (await ListAsync(db, partnerId)).FirstOrDefault(p => p.SubscriptionId == subscriptionId)
        ?? throw new SW.PrimitiveTypes.SWNotFoundException(subscriptionId.ToString());

    /// <summary>
    /// Whether the partner can have their own mapping of it: when it maps with the native mapper,
    /// or doesn't map at all. Any other mapper is ours to change.
    /// </summary>
    public static bool Customizable(Subscription subscription) =>
        subscription.MapperId is null || subscription.MapperId == PartnerMappingRules.MapperId;
}
