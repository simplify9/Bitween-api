using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.Services;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Portal;

/// <summary>
/// Their own partner properties, only those we show them: values in clear but a secret one's, which
/// they may replace without reading. With a change of theirs waiting for approval, or rejected.
/// </summary>
[HandlerName("properties")]
public class Properties(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.PropertiesView);
        var partner = await dbContext.Set<Partner>().AsNoTracking().SingleAsync(p => p.Id == caller.PartnerId);
        var visible = PartnerPortalAccess.VisibleKeys(partner);
        var editable = caller.Can(Model.Permissions.Portal.PropertiesEdit)
            ? PartnerPortalAccess.EditableKeys(partner)
            : new HashSet<string>();
        var secret = new HashSet<string>(partner.SecretProperties ?? [], StringComparer.OrdinalIgnoreCase);

        var waiting = await Services.PartnerPropertyChanges.WaitingAsync(dbContext, caller.PartnerId);
        var latest = await dbContext.Set<PartnerPropertyChange>().AsNoTracking()
            .Where(c => c.PartnerId == caller.PartnerId).OrderByDescending(c => c.Id).FirstOrDefaultAsync();

        return new PortalProperties
        {
            Properties = (partner.AdapterProperties ?? new Dictionary<string, string>())
                .Where(p => visible.Contains(p.Key))
                .OrderBy(p => p.Key)
                .Select(p => new PortalProperty
                {
                    Key = p.Key,
                    Value = secret.Contains(p.Key) ? null : p.Value,
                    Secret = secret.Contains(p.Key),
                    Editable = editable.Contains(p.Key)
                })
                .ToList(),
            NeedsApproval = partner.ChangesNeedApproval,
            Waiting = waiting is null
                ? null
                : new PortalPropertiesWaiting
                {
                    // Only what they may still see, a secret without its value.
                    Values = waiting.Values.Where(v => visible.Contains(v.Key))
                        .ToDictionary(v => v.Key, v => secret.Contains(v.Key) ? null : v.Value),
                    RequestedOn = waiting.RequestedOn
                },
            Rejected = latest is { Status: PartnerChangeStatus.Rejected }
                ? new PortalMappingRejected { Note = latest.Note, DecidedOn = latest.DecidedOn ?? latest.RequestedOn }
                : null
        };
    }
}

/// <summary>
/// Changes the properties they may change: used at once, or waiting for our approval when their
/// changes need it. Only what really changes is kept; a secret left empty keeps its value.
/// </summary>
[HandlerName("saveproperties")]
public class SaveProperties(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
    : ICommandHandler<PortalSaveProperties, object>
{
    /// <summary>Longer than any address, key or name a partner property holds.</summary>
    public const int MaxValueLength = 4000;

    public async Task<object> Handle(PortalSaveProperties request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.PropertiesEdit);
        var partner = await dbContext.Set<Partner>().SingleAsync(p => p.Id == caller.PartnerId);
        var secret = new HashSet<string>(partner.SecretProperties ?? [], StringComparer.OrdinalIgnoreCase);
        var stored = new Dictionary<string, string>(partner.AdapterProperties ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        var submitted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in request.Values ?? new Dictionary<string, string>())
        {
            if (secret.Contains(key) && (string.IsNullOrEmpty(value) || value == AdapterSecretProperties.Sentinel)) continue;
            if ((value ?? "").Length > MaxValueLength)
                throw new SWValidationException("VALUE_TOO_LONG", $"{key} is longer than {MaxValueLength} characters.");
            submitted[key] = value ?? "";
        }
        // Before anything is compared with what is stored, so a refusal or a success never says
        // whether a guess at a hidden or secret value was right.
        Services.PartnerPropertyChanges.EnsureEditable(partner, submitted);

        // A secret counts as changed whatever it is, for the same reason; the rest only when different.
        var changed = submitted.Where(v => secret.Contains(v.Key) || !stored.TryGetValue(v.Key, out var now) || now != v.Value)
            .ToDictionary(v => v.Key, v => v.Value);

        // A secret replacement still waiting, left empty here because it can't be shown, is carried
        // into this change rather than taken back by it.
        var waiting = await Services.PartnerPropertyChanges.WaitingAsync(dbContext, caller.PartnerId);
        if (waiting is not null && changed.Count > 0)
        {
            var editable = PartnerPortalAccess.EditableKeys(partner);
            foreach (var (key, value) in waiting.Values)
                if (secret.Contains(key) && editable.Contains(key) && !submitted.ContainsKey(key))
                    changed[key] = value;
        }
        if (changed.Count == 0) return new PortalMappingSaved();

        var waits = await Services.PartnerPropertyChanges.RequestAsync(dbContext, cache, partner, changed, requestContext.GetNameIdentifier());
        return new PortalMappingSaved { Waiting = waits };
    }
}

/// <summary>Takes back their properties change waiting for approval.</summary>
[HandlerName("withdrawproperties")]
public class WithdrawProperties(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<PortalWithdrawProperties, object>
{
    public async Task<object> Handle(PortalWithdrawProperties request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.PropertiesEdit);
        var waiting = await Services.PartnerPropertyChanges.WaitingAsync(dbContext, caller.PartnerId);
        if (waiting is not null)
            await Services.PartnerPropertyChanges.DecideAsync(dbContext, waiting.Id, PartnerChangeStatus.Withdrawn,
                requestContext.GetNameIdentifier());
        return null;
    }
}
