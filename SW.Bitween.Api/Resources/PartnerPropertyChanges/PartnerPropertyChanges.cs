using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.Resources.PartnerChanges;
using SW.Bitween.Services;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.PartnerPropertyChanges;

/// <summary>
/// One change to a partner's own properties: each value now beside what it would become, a secret
/// one never shown, and whether it can be approved as things are.
/// </summary>
public class Get(BitweenDbContext dbContext, RequestContext requestContext) : IGetHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.PartnerChanges.View);
        var change = await dbContext.Set<PartnerPropertyChange>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == key)
                     ?? throw new SWNotFoundException(key.ToString());
        var partner = await dbContext.Set<Partner>().AsNoTracking().SingleAsync(p => p.Id == change.PartnerId);
        var secret = new HashSet<string>(partner.SecretProperties ?? [], StringComparer.OrdinalIgnoreCase);
        var now = new Dictionary<string, string>(partner.AdapterProperties ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        string problem = null;
        if (change.Status == PartnerChangeStatus.Waiting)
            try
            {
                Services.PartnerPropertyChanges.EnsureEditable(partner, change.Values);
            }
            catch (SWValidationException ex)
            {
                problem = ex.Message;
            }

        var names = await Read.AccountNames(dbContext, [change.RequestedBy, change.DecidedBy]);
        var detail = Read.Fill(new PartnerPropertyChangeDetail(), change, partner.Name, names);
        detail.Items = change.Values.OrderBy(v => v.Key).Select(v => new PartnerPropertyChangeItem
        {
            Key = v.Key,
            Secret = secret.Contains(v.Key),
            Current = secret.Contains(v.Key) ? null : now.GetValueOrDefault(v.Key),
            Proposed = secret.Contains(v.Key) ? null : v.Value
        }).ToList();
        detail.Problem = problem;
        return detail;
    }
}

/// <summary>Approves a waiting change to a partner's properties: their values are changed now.</summary>
[HandlerName("approve")]
public class Approve(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
    : ICommandHandler<int, PartnerChangeDecision, object>
{
    public async Task<object> Handle(int key, PartnerChangeDecision request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.PartnerChanges.Operate);
        var change = await dbContext.Set<PartnerPropertyChange>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == key)
                     ?? throw new SWNotFoundException(key.ToString());
        if (change.Status != PartnerChangeStatus.Waiting) throw Read.AlreadyDecided();

        var partner = await dbContext.Set<Partner>().SingleAsync(p => p.Id == change.PartnerId);
        // Checked again now: what they may change may have changed since they asked.
        Services.PartnerPropertyChanges.EnsureEditable(partner, change.Values);

        // Decided and applied together, or not at all: a decision made meanwhile wins, and this one stops.
        var by = requestContext.GetNameIdentifier();
        await PartnerChangeTransaction.RunAsync(dbContext, cache, async () =>
        {
            if (!await Services.PartnerPropertyChanges.DecideAsync(dbContext, key, PartnerChangeStatus.Approved, by))
                throw Read.AlreadyDecided();
            Services.PartnerPropertyChanges.Apply(partner, change.Values);
            await dbContext.SaveChangesAsync();
            return true;
        });
        return null;
    }
}

/// <summary>Rejects a waiting change to a partner's properties. They keep what they have, and see why.</summary>
[HandlerName("reject")]
public class Reject(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, PartnerChangeDecision, object>
{
    public async Task<object> Handle(int key, PartnerChangeDecision request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.PartnerChanges.Operate);
        if (!await dbContext.Set<PartnerPropertyChange>().AnyAsync(c => c.Id == key))
            throw new SWNotFoundException(key.ToString());
        if (!await Services.PartnerPropertyChanges.DecideAsync(dbContext, key, PartnerChangeStatus.Rejected,
                requestContext.GetNameIdentifier(), string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()))
            throw Read.AlreadyDecided();
        return null;
    }

    private class Validate : AbstractValidator<PartnerChangeDecision>
    {
        public Validate() => RuleFor(i => i.Note).MaximumLength(1000);
    }
}
