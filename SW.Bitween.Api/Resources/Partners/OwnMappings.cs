using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Partners;

/// <summary>
/// The subscriptions this partner maps their own way, made in the partner portal, and those with a
/// change of theirs waiting: what their partner page lists, each opening the subscription where the
/// rules and history are.
/// </summary>
[HandlerName("ownmappings")]
public class OwnMappings(BitweenDbContext dbContext, RequestContext requestContext) : IGetHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Partners.View);

        var mappings = await (from m in dbContext.Set<PartnerMapping>().AsNoTracking()
                join s in dbContext.Set<Subscription>() on m.SubscriptionId equals s.Id
                where m.PartnerId == key
                select new { m.SubscriptionId, s.Name, m.CreatedOn, m.CreatedBy, m.ModifiedOn, m.ModifiedBy })
            .ToListAsync();
        var waiting = await (from c in dbContext.Set<PartnerMappingChange>().AsNoTracking()
                join s in dbContext.Set<Subscription>() on c.SubscriptionId equals s.Id
                where c.PartnerId == key && c.Status == PartnerChangeStatus.Waiting
                select new { c.SubscriptionId, s.Name })
            .Distinct()
            .ToListAsync();

        // Changed by an account id; shown by name, as the trail shows them.
        var ids = mappings.Select(m => int.TryParse(m.ModifiedBy ?? m.CreatedBy, out var id) ? id : (int?)null)
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var names = await dbContext.Set<Account>().AsNoTracking()
            .Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id.ToString(), a => a.DisplayName);
        var waitingIds = waiting.Select(w => w.SubscriptionId).ToHashSet();

        return mappings.Select(m => new PartnerOwnMapping
            {
                SubscriptionId = m.SubscriptionId,
                SubscriptionName = m.Name,
                ChangedOn = m.ModifiedOn ?? m.CreatedOn,
                ChangedBy = names.GetValueOrDefault(m.ModifiedBy ?? m.CreatedBy ?? ""),
                InUse = true,
                Waiting = waitingIds.Contains(m.SubscriptionId)
            })
            .Concat(waiting.Where(w => mappings.All(m => m.SubscriptionId != w.SubscriptionId))
                .Select(w => new PartnerOwnMapping { SubscriptionId = w.SubscriptionId, SubscriptionName = w.Name, Waiting = true }))
            .OrderBy(m => m.SubscriptionName)
            .ToList();
    }
}
