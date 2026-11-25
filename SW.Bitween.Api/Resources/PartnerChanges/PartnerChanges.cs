using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.Bitween.Services;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.PartnerChanges;

/// <summary>Reading changes, as our team's pages show them.</summary>
internal static class Read
{
    public class Row
    {
        public PartnerMappingChange Change { get; init; }
        public string SubscriptionName { get; init; }
        public string PartnerName { get; init; }
    }

    public static IQueryable<Row> Rows(BitweenDbContext db) =>
        from c in db.Set<PartnerMappingChange>().AsNoTracking()
        join s in db.Set<Subscription>() on c.SubscriptionId equals s.Id
        join p in db.Set<Partner>() on c.PartnerId equals p.Id
        select new Row { Change = c, SubscriptionName = s.Name, PartnerName = p.Name };

    /// <summary>Accounts by the id changes record them by, to the name they're shown by.</summary>
    public static async Task<Dictionary<string, string>> AccountNames(BitweenDbContext db, IEnumerable<string> ids)
    {
        var numeric = ids.Where(id => id != null).Distinct()
            .Select(id => int.TryParse(id, out var n) ? n : (int?)null)
            .Where(n => n != null).Select(n => n.Value).ToList();
        return await db.Set<Account>().AsNoTracking()
            .Where(a => numeric.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id.ToString(), a => a.DisplayName);
    }

    public static T Fill<T>(T row, PartnerMappingChange c, string subscriptionName, string partnerName,
        IReadOnlyDictionary<string, string> names) where T : PartnerChangeRow
    {
        row.Id = c.Id;
        row.SubscriptionId = c.SubscriptionId;
        row.SubscriptionName = subscriptionName;
        row.PartnerId = c.PartnerId;
        row.PartnerName = partnerName;
        row.Status = c.Status.ToString().ToLowerInvariant();
        row.BackToOurs = c.BackToOurs;
        row.RequestedOn = c.RequestedOn;
        row.RequestedBy = names.GetValueOrDefault(c.RequestedBy ?? "");
        row.DecidedOn = c.DecidedOn;
        row.DecidedBy = names.GetValueOrDefault(c.DecidedBy ?? "");
        row.Note = c.Note;
        return row;
    }

    public static SWValidationException AlreadyDecided() =>
        new("ALREADY_DECIDED", "This change was already approved, rejected or taken back. It may have been replaced by a newer one.");

    public static string RulesOf(IReadOnlyDictionary<string, string> properties) =>
        properties?.GetValueOrDefault(PartnerMappingRules.RulesKey) ?? "";

    /// <summary>
    /// Refuses rules that can't be used for the partner as things stand: the subscription no longer
    /// maps with the native mapper, or the rules read a global value ours no longer sends them.
    /// </summary>
    public static async Task EnsureUsable(BitweenDbContext db, PartnerMappingChange change)
    {
        if (change.BackToOurs) return;
        var subscription = await db.Set<Subscription>().AsNoTracking().SingleAsync(s => s.Id == change.SubscriptionId);
        if (!Portal.PortalPlaces.Customizable(subscription))
            throw new SWValidationException("NOT_CUSTOMIZABLE",
                "This subscription no longer maps with the native mapper, so a partner's own mapping can't be used.");
        PartnerMappingRules.Validate(change.MapperProperties.ToDictionary(), subscription.MapperProperties);
    }
}

/// <summary>The changes waiting for approval, oldest first; or the latest decided, newest first.</summary>
public class Search(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<PartnerChangeSearch, object>
{
    /// <summary>More than anyone works through at once; the rest show as these are decided.</summary>
    public const int Max = 200;

    public async Task<object> Handle(PartnerChangeSearch request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.PartnerChanges.View);
        var rows = Read.Rows(dbContext);
        var found = request.Waiting
            ? await rows.Where(r => r.Change.Status == PartnerMappingChangeStatus.Waiting)
                .OrderBy(r => r.Change.RequestedOn).Take(Max).ToListAsync()
            : await rows.Where(r => r.Change.Status == PartnerMappingChangeStatus.Approved ||
                                    r.Change.Status == PartnerMappingChangeStatus.Rejected)
                .OrderByDescending(r => r.Change.DecidedOn).Take(Max).ToListAsync();

        var names = await Read.AccountNames(dbContext,
            found.SelectMany(r => new[] { r.Change.RequestedBy, r.Change.DecidedBy }));
        return found.Select(r => Read.Fill(new PartnerChangeRow(), r.Change, r.SubscriptionName, r.PartnerName, names)).ToList();
    }
}

/// <summary>How many changes wait for approval, for the count beside the page's name.</summary>
[HandlerName("count")]
public class Count(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<object>
{
    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.PartnerChanges.View);
        return await dbContext.Set<PartnerMappingChange>()
            .CountAsync(c => c.Status == PartnerMappingChangeStatus.Waiting);
    }
}

/// <summary>One change, beside what is used for the partner now, and whether it can be approved.</summary>
public class Get(BitweenDbContext dbContext, RequestContext requestContext) : IGetHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.PartnerChanges.View);
        var row = await Read.Rows(dbContext).Where(r => r.Change.Id == key).SingleOrDefaultAsync()
                  ?? throw new SWNotFoundException(key.ToString());
        var change = row.Change;

        var theirs = await dbContext.Set<PartnerMapping>().AsNoTracking()
            .SingleOrDefaultAsync(m => m.SubscriptionId == change.SubscriptionId && m.PartnerId == change.PartnerId);
        var ours = await dbContext.Set<Subscription>().AsNoTracking()
            .Where(s => s.Id == change.SubscriptionId).Select(s => s.MapperProperties).SingleAsync();

        string problem = null;
        if (change.Status == PartnerMappingChangeStatus.Waiting)
            try
            {
                await Read.EnsureUsable(dbContext, change);
            }
            catch (SWValidationException ex)
            {
                problem = ex.Message;
            }

        var names = await Read.AccountNames(dbContext, [change.RequestedBy, change.DecidedBy]);
        var detail = Read.Fill(new PartnerChangeDetail(), change, row.SubscriptionName, row.PartnerName, names);
        detail.MappingRules = Read.RulesOf(change.MapperProperties);
        // What their exchanges really get: theirs only while it still fits ours, as the cache decides.
        var theirsUsed = theirs is not null && PartnerMappingRules.Fits(theirs, ours);
        detail.CurrentRules = Read.RulesOf(theirsUsed ? theirs.MapperProperties : ours);
        detail.CurrentIsOurs = !theirsUsed;
        detail.Problem = problem;
        return detail;
    }
}

/// <summary>Approves a waiting change: it is used for the partner from their next exchange.</summary>
[HandlerName("approve")]
public class Approve(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, PartnerChangeDecision, object>
{
    public async Task<object> Handle(int key, PartnerChangeDecision request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.PartnerChanges.Operate);
        var change = await dbContext.Set<PartnerMappingChange>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == key)
                     ?? throw new SWNotFoundException(key.ToString());
        if (change.Status != PartnerMappingChangeStatus.Waiting) throw Read.AlreadyDecided();

        // Checked again now: ours may have changed since the partner asked.
        await Read.EnsureUsable(dbContext, change);

        // Claimed first, so a decision made meanwhile wins and this one stops here. The change is
        // the one the reviewer opened: a newer ask is a row of its own, and replacing this one took it back.
        var by = requestContext.GetNameIdentifier();
        if (!await PartnerMappingChanges.DecideAsync(dbContext, key, PartnerMappingChangeStatus.Approved, by))
            throw Read.AlreadyDecided();
        try
        {
            await PartnerMappingChanges.ApplyAsync(dbContext, change.SubscriptionId, change.PartnerId, change.MapperProperties);
            // Changing a partner mapping clears the cache, this node's and the others', after the commit.
            await dbContext.SaveChangesAsync();
        }
        catch
        {
            await PartnerMappingChanges.UndecideAsync(dbContext, key);
            throw;
        }
        return null;
    }
}

/// <summary>Rejects a waiting change. The partner keeps what they have, and sees why.</summary>
[HandlerName("reject")]
public class Reject(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, PartnerChangeDecision, object>
{
    public async Task<object> Handle(int key, PartnerChangeDecision request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.PartnerChanges.Operate);
        if (!await dbContext.Set<PartnerMappingChange>().AnyAsync(c => c.Id == key))
            throw new SWNotFoundException(key.ToString());
        if (!await PartnerMappingChanges.DecideAsync(dbContext, key, PartnerMappingChangeStatus.Rejected,
                requestContext.GetNameIdentifier(), string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()))
            throw Read.AlreadyDecided();
        return null;
    }

    private class Validate : AbstractValidator<PartnerChangeDecision>
    {
        public Validate() => RuleFor(i => i.Note).MaximumLength(1000);
    }
}

/// <summary>A partner's mapping of one subscription over time, newest first.</summary>
[HandlerName("history")]
public class History(BitweenDbContext dbContext, RequestContext requestContext)
    : IQueryHandler<PartnerChangeHistoryRequest, object>
{
    public async Task<object> Handle(PartnerChangeHistoryRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.PartnerChanges.View);
        var found = await Read.Rows(dbContext)
            .Where(r => r.Change.SubscriptionId == request.SubscriptionId && r.Change.PartnerId == request.PartnerId)
            .OrderByDescending(r => r.Change.Id)
            .ToListAsync();
        var names = await Read.AccountNames(dbContext,
            found.SelectMany(r => new[] { r.Change.RequestedBy, r.Change.DecidedBy }));

        return found.Select(r =>
        {
            var version = Read.Fill(new PartnerChangeVersion(), r.Change, r.SubscriptionName, r.PartnerName, names);
            version.MappingRules = Read.RulesOf(r.Change.MapperProperties);
            version.CanPutBack = !r.Change.BackToOurs &&
                                 r.Change.Status is PartnerMappingChangeStatus.Live or PartnerMappingChangeStatus.Approved;
            return version;
        }).ToList();
    }
}

/// <summary>
/// Puts an earlier version of a partner's mapping back, for when a change broke their deliveries.
/// Our team's, so it is used at once, and becomes the newest entry of the history.
/// </summary>
[HandlerName("putback")]
public class PutBack(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, PartnerChangeDecision, object>
{
    public async Task<object> Handle(int key, PartnerChangeDecision request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.PartnerChanges.Operate);
        var version = await dbContext.Set<PartnerMappingChange>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == key)
                      ?? throw new SWNotFoundException(key.ToString());
        if (version.BackToOurs || version.Status is not (PartnerMappingChangeStatus.Live or PartnerMappingChangeStatus.Approved))
            throw new SWValidationException("NOT_A_VERSION", "Only a version that was used for the partner can be put back.");

        await Read.EnsureUsable(dbContext, version);
        await PartnerMappingChanges.ApplyAsync(dbContext, version.SubscriptionId, version.PartnerId, version.MapperProperties);
        dbContext.Add(new PartnerMappingChange(version.SubscriptionId, version.PartnerId, version.MapperProperties.ToDictionary(),
            PartnerMappingChangeStatus.Live, requestContext.GetNameIdentifier(),
            $"Put back the version of {version.RequestedOn:yyyy-MM-dd HH:mm} UTC."));
        await dbContext.SaveChangesAsync();
        return null;
    }
}
