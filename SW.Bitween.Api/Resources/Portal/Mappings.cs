using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.Resources.MappingPreviews;
using SW.Bitween.Services;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Portal;

/// <summary>Every place the signed-in partner is connected, and whether they map it their own way.</summary>
[HandlerName("mappings")]
public class Mappings(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.MappingsView);
        var places = await PortalPlaces.ListAsync(dbContext, caller.PartnerId);
        var ids = places.Select(p => p.SubscriptionId).ToList();

        var subscriptions = await (from s in dbContext.Set<Subscription>().AsNoTracking()
                join d in dbContext.Set<Document>() on s.DocumentId equals d.Id
                where ids.Contains(s.Id)
                select new { s.Id, s.Name, s.MapperId, DocumentName = d.Name })
            .ToDictionaryAsync(s => s.Id);
        var theirs = await dbContext.Set<PartnerMapping>().AsNoTracking()
            .Where(m => m.PartnerId == caller.PartnerId && ids.Contains(m.SubscriptionId))
            .ToDictionaryAsync(m => m.SubscriptionId, m => m.ModifiedOn ?? m.CreatedOn);

        return places.Where(p => subscriptions.ContainsKey(p.SubscriptionId))
            .Select(p =>
            {
                var s = subscriptions[p.SubscriptionId];
                return new PortalMappingPlace
                {
                    SubscriptionId = s.Id,
                    SubscriptionName = s.Name,
                    Via = p.Via,
                    DocumentName = s.DocumentName,
                    Customized = theirs.ContainsKey(s.Id),
                    Customizable = s.MapperId is null || s.MapperId == PartnerMappingRules.MapperId,
                    ModifiedOn = theirs.TryGetValue(s.Id, out var on) ? on : null
                };
            })
            .OrderBy(p => p.SubscriptionName)
            .ToList();
    }
}

/// <summary>One place's mapping: the subscription's, as the starting point, and theirs if they have one.</summary>
[HandlerName("mapping")]
public class Mapping(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<PortalMappingRequest, object>
{
    public async Task<object> Handle(PortalMappingRequest request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.MappingsView);
        await PortalPlaces.FindAsync(dbContext, caller.PartnerId, request.SubscriptionId);
        var subscription = await dbContext.Set<Subscription>().AsNoTracking().SingleAsync(s => s.Id == request.SubscriptionId);
        var theirs = await dbContext.Set<PartnerMapping>().AsNoTracking()
            .SingleOrDefaultAsync(m => m.SubscriptionId == request.SubscriptionId && m.PartnerId == caller.PartnerId);
        var customizable = PortalPlaces.Customizable(subscription);

        return new PortalMapping
        {
            SubscriptionId = subscription.Id,
            SubscriptionName = subscription.Name,
            Customizable = customizable,
            Default = customizable && subscription.MapperId is not null
                ? new Dictionary<string, string>
                {
                    [PartnerMappingRules.Keys[0]] = subscription.MapperProperties?.GetValueOrDefault(PartnerMappingRules.Keys[0]) ?? ""
                }
                : null,
            Theirs = theirs?.MapperProperties.ToDictionary()
        };
    }
}

/// <summary>Saves their own version of a place's mapping, used for them from their next exchange.</summary>
[HandlerName("savemapping")]
public class SaveMapping(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
    : ICommandHandler<PortalSaveMapping, object>
{
    public async Task<object> Handle(PortalSaveMapping request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.MappingsEdit);
        await PortalPlaces.FindAsync(dbContext, caller.PartnerId, request.SubscriptionId);
        var subscription = await dbContext.Set<Subscription>().AsNoTracking().SingleAsync(s => s.Id == request.SubscriptionId);
        if (!PortalPlaces.Customizable(subscription))
            throw new SWValidationException("NOT_CUSTOMIZABLE",
                "This mapping uses a mapper that can't be changed here. Ask us to change it.");

        var properties = PartnerMappingRules.Validate(request.MapperProperties, subscription.MapperProperties);
        var theirs = await dbContext.Set<PartnerMapping>()
            .SingleOrDefaultAsync(m => m.SubscriptionId == request.SubscriptionId && m.PartnerId == caller.PartnerId);
        if (theirs is null)
            dbContext.Add(new PartnerMapping(request.SubscriptionId, caller.PartnerId, properties));
        else
            theirs.SetMapperProperties(properties);

        await dbContext.SaveChangesAsync();
        await cache.BroadcastRevoke();
        return null;
    }
}

/// <summary>Removes their own version, so they get the subscription's mapping again.</summary>
[HandlerName("resetmapping")]
public class ResetMapping(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
    : ICommandHandler<PortalMappingRequest, object>
{
    public async Task<object> Handle(PortalMappingRequest request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.MappingsEdit);
        var theirs = await dbContext.Set<PartnerMapping>()
            .SingleOrDefaultAsync(m => m.SubscriptionId == request.SubscriptionId && m.PartnerId == caller.PartnerId);
        if (theirs is not null)
        {
            dbContext.Remove(theirs);
            await dbContext.SaveChangesAsync();
            await cache.BroadcastRevoke();
        }
        return null;
    }
}

/// <summary>
/// Maps a sample with unsaved rules, against their own partner values. Refuses global values the
/// subscription's mapping doesn't send them, as saving does.
/// </summary>
[HandlerName("previewmapping")]
public class PreviewMapping(BitweenDbContext dbContext, RequestContext requestContext, MappingContextFactory contextFactory)
    : ICommandHandler<PortalPreviewMapping, object>
{
    public async Task<object> Handle(PortalPreviewMapping request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.MappingsEdit);
        await PortalPlaces.FindAsync(dbContext, caller.PartnerId, request.SubscriptionId);
        var subscription = await dbContext.Set<Subscription>().AsNoTracking().SingleAsync(s => s.Id == request.SubscriptionId);
        try
        {
            PartnerMappingRules.Validate(
                new Dictionary<string, string> { [PartnerMappingRules.Keys[0]] = request.MappingRules ?? "" },
                subscription.MapperProperties);
        }
        catch (SWValidationException ex)
        {
            return new MappingPreviewResponse { Error = ex.Message };
        }

        return await Preview.Run(new MappingPreviewRequest
        {
            MappingRules = request.MappingRules,
            SourceDocument = request.SourceDocument,
            PartnerId = caller.PartnerId
        }, contextFactory);
    }
}

/// <summary>Their latest exchanges through a place, to take a sample from.</summary>
[HandlerName("mappingsamples")]
public class MappingSamples(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<PortalMappingRequest, object>
{
    public async Task<object> Handle(PortalMappingRequest request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.ExchangeFilesView);
        return await dbContext.Set<Xchange>().AsNoTracking()
            .Where(x => x.PartnerId == caller.PartnerId && x.SubscriptionId == request.SubscriptionId && x.InputSize > 0)
            .OrderByDescending(x => x.StartedOn)
            .Take(10)
            .Select(x => new PortalMappingSample { XchangeId = x.Id, StartedOn = x.StartedOn, InputName = x.InputName })
            .ToListAsync();
    }
}

public class PortalMappingSampleRequest
{
    public string XchangeId { get; set; } = "";
}

/// <summary>What one of their exchanges received, as a sample. Theirs only, and not too big to edit against.</summary>
[HandlerName("mappingsample")]
public class MappingSample(BitweenDbContext dbContext, RequestContext requestContext, XchangeService xchangeService)
    : IQueryHandler<PortalMappingSampleRequest, object>
{
    /// <summary>Bigger than any sample worth editing against; the editor slows well before this.</summary>
    public const int MaxBytes = 1_000_000;

    public async Task<object> Handle(PortalMappingSampleRequest request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.ExchangeFilesView);
        var xchange = await dbContext.Set<Xchange>().AsNoTracking()
                          .SingleOrDefaultAsync(x => x.Id == request.XchangeId && x.PartnerId == caller.PartnerId)
                      ?? throw new SWNotFoundException(request.XchangeId);
        if (xchange.InputSize > MaxBytes)
            throw new SWValidationException("SAMPLE_TOO_BIG", "That exchange is too big to use as a sample. Pick a smaller one.");
        return new { Data = await xchangeService.GetFile(xchange, XchangeFileType.Input) };
    }
}

/// <summary>The names of their own partner properties, for a mapping rule to read; never the values.</summary>
[HandlerName("propertykeys")]
public class PropertyKeys(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.MappingsEdit);
        var properties = await dbContext.Set<Partner>().AsNoTracking()
            .Where(p => p.Id == caller.PartnerId).Select(p => p.AdapterProperties).SingleOrDefaultAsync();
        return (properties ?? new Dictionary<string, string>()).Keys.OrderBy(k => k).ToList();
    }
}
