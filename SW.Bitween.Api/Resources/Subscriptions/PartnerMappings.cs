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
                orderby p.Name
                select new { m, PartnerName = p.Name })
            .ToListAsync();

        // Changed by an account id; shown by name, the way the trail shows them.
        var ids = rows.Select(r => r.m.ModifiedBy ?? r.m.CreatedBy)
            .Select(v => int.TryParse(v, out var id) ? id : (int?)null)
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var names = await dbContext.Set<Account>().AsNoTracking()
            .Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id.ToString(), a => a.DisplayName);

        return rows.Select(r => new SubscriptionPartnerMapping
        {
            PartnerId = r.m.PartnerId,
            PartnerName = r.PartnerName,
            MappingRules = r.m.MapperProperties.GetValueOrDefault(PartnerMappingRules.RulesKey) ?? "",
            ChangedOn = r.m.ModifiedOn ?? r.m.CreatedOn,
            ChangedBy = names.GetValueOrDefault(r.m.ModifiedBy ?? r.m.CreatedBy ?? ""),
            SetAside = !PartnerMappingRules.Fits(r.m, ours)
        }).ToList();
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
        // Clears the cache, this node's and the others', like a partner's change.
        await dbContext.SaveChangesAsync();
        return null;
    }

    private class Validate : AbstractValidator<RemovePartnerMappingModel>
    {
        public Validate() => RuleFor(i => i.PartnerId).GreaterThan(0);
    }
}
