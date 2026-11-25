using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Portal;

/// <summary>
/// The subscriptions that run for the signed-in partner: their own, the ones a gateway runs for
/// them alongside other partners, and the response subscriptions any of those hand their replies to.
/// </summary>
[HandlerName("subscriptions")]
public class Subscriptions(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.SubscriptionsView);
        var all = await IdsAsync(dbContext, caller.PartnerId);
        var rows = await (from s in dbContext.Set<Subscription>().AsNoTracking()
                join d in dbContext.Set<Document>() on s.DocumentId equals d.Id
                where all.Contains(s.Id)
                orderby s.Name
                select new PortalSubscriptionRow
                {
                    Id = s.Id,
                    Name = s.Name,
                    Type = s.Type.ToString(),
                    DocumentName = d.Name,
                    Paused = s.Inactive
                })
            .ToListAsync();

        return rows;
    }

    /// <summary>The ids of the subscriptions that run for a partner, as listed above.</summary>
    internal static async Task<List<int>> IdsAsync(BitweenDbContext dbContext, int partnerId)
    {
        var owned = await dbContext.Set<Subscription>().AsNoTracking()
            .Where(s => s.PartnerId == partnerId).Select(s => s.Id).ToListAsync();
        var viaGateways = await dbContext.Set<ApiGatewayPartner>().AsNoTracking()
            .Where(g => g.PartnerId == partnerId).Select(g => g.SubscriptionId).ToListAsync();
        var viaRoutes = await dbContext.Set<BusGatewayRoute>().AsNoTracking()
            .Where(r => r.PartnerId == partnerId).Select(r => r.SubscriptionId).ToListAsync();

        // A response subscription runs as the partner of the one that handed it the reply.
        var reached = owned.Concat(viaGateways).Concat(viaRoutes).Distinct().ToList();
        var responses = await dbContext.Set<Subscription>().AsNoTracking()
            .Where(s => reached.Contains(s.Id) && s.ResponseSubscriptionId != null)
            .Select(s => s.ResponseSubscriptionId!.Value).ToListAsync();

        return reached.Concat(responses).Distinct().ToList();
    }
}

/// <summary>
/// The information types of what runs for them, with the names of the values each promotes, for
/// their Exchanges page's filters. Not where in the document a value is read from: that is ours.
/// </summary>
[HandlerName("informationtypes")]
public class InformationTypes(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.ExchangesView);
        var ids = await Subscriptions.IdsAsync(dbContext, caller.PartnerId);
        // The ids first: told apart by id rather than by every column, some of which are JSON.
        var documentIds = await dbContext.Set<Subscription>().AsNoTracking()
            .Where(s => ids.Contains(s.Id)).Select(s => s.DocumentId).Distinct().ToListAsync();
        var documents = await dbContext.Set<Document>().AsNoTracking()
            .Where(d => documentIds.Contains(d.Id)).ToListAsync();
        return documents.OrderBy(d => d.Name).Select(d => new PortalInformationType
        {
            Id = d.Id,
            Name = d.Name,
            Code = d.Code,
            PromotedPropertyKeys = (d.PromotedProperties ?? new Dictionary<string, string>()).Keys.ToList()
        }).ToList();
    }
}
