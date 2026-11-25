using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Subscriptions;

/// <summary>
/// The partners using their own mapping of this subscription, made in the partner portal, with who
/// last changed each and when. So the subscription's page says why one partner receives something
/// different from the rest.
/// </summary>
[HandlerName("partnermappings")]
public class PartnerMappings(BitweenDbContext dbContext, RequestContext requestContext) : IGetHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);
        var ours = await dbContext.Set<Subscription>().AsNoTracking()
            .Where(s => s.Id == key).Select(s => s.MapperProperties).SingleOrDefaultAsync();

        var rows = await (from m in dbContext.Set<PartnerMapping>().AsNoTracking()
                join p in dbContext.Set<Partner>() on m.PartnerId equals p.Id
                where m.SubscriptionId == key
                select new { m, PartnerName = p.Name })
            .ToListAsync();
        // A partner whose change waits is listed too, with or without a mapping of their own yet.
        var waiting = await (from c in dbContext.Set<PartnerMappingChange>().AsNoTracking()
                join p in dbContext.Set<Partner>() on c.PartnerId equals p.Id
                where c.SubscriptionId == key && c.Status == PartnerMappingChangeStatus.Waiting
                select new { c.Id, c.PartnerId, PartnerName = p.Name })
            .ToListAsync();
        var waitingOf = waiting.GroupBy(w => w.PartnerId).ToDictionary(g => g.Key, g => g.Max(w => w.Id));

        // Changed by an account id; shown by name, the way the trail shows them.
        var ids = rows.Select(r => r.m.ModifiedBy ?? r.m.CreatedBy)
            .Select(v => int.TryParse(v, out var id) ? id : (int?)null)
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var names = await dbContext.Set<Account>().AsNoTracking()
            .Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id.ToString(), a => a.DisplayName);

        var inUse = rows.Select(r => new SubscriptionPartnerMapping
        {
            PartnerId = r.m.PartnerId,
            PartnerName = r.PartnerName,
            MappingRules = r.m.MapperProperties.GetValueOrDefault(PartnerMappingRules.RulesKey) ?? "",
            ChangedOn = r.m.ModifiedOn ?? r.m.CreatedOn,
            ChangedBy = names.GetValueOrDefault(r.m.ModifiedBy ?? r.m.CreatedBy ?? ""),
            SetAside = !PartnerMappingRules.Fits(r.m, ours),
            InUse = true,
            WaitingChangeId = waitingOf.TryGetValue(r.m.PartnerId, out var w) ? w : null
        });
        var onlyWaiting = waiting.Where(w => rows.All(r => r.m.PartnerId != w.PartnerId))
            .GroupBy(w => w.PartnerId)
            .Select(g => new SubscriptionPartnerMapping
            {
                PartnerId = g.Key,
                PartnerName = g.First().PartnerName,
                WaitingChangeId = g.Max(x => x.Id)
            });
        return inUse.Concat(onlyWaiting).OrderBy(r => r.PartnerName).ToList();
    }
}

/// <summary>
/// Removes a partner's own mapping, so they get the subscription's again from their next exchange.
/// For when theirs breaks something and they can't fix it themselves.
/// </summary>
[HandlerName("removepartnermapping")]
public class RemovePartnerMapping(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, RemovePartnerMappingModel, object>
{
    public async Task<object> Handle(int key, RemovePartnerMappingModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.Edit);
        var mapping = await dbContext.Set<PartnerMapping>()
                          .SingleOrDefaultAsync(m => m.SubscriptionId == key && m.PartnerId == request.PartnerId)
                      ?? throw new SWNotFoundException($"{key}/{request.PartnerId}");
        dbContext.Remove(mapping);
        // In their mapping's history like any change; ours, so used at once.
        dbContext.Add(new PartnerMappingChange(key, request.PartnerId, null, PartnerMappingChangeStatus.Live,
            requestContext.GetNameIdentifier()));
        // Clears the cache, this node's and the others', like a partner's change.
        await dbContext.SaveChangesAsync();
        return null;
    }

    private class Validate : AbstractValidator<RemovePartnerMappingModel>
    {
        public Validate() => RuleFor(i => i.PartnerId).GreaterThan(0);
    }
}
