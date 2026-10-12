using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Subscriptions;

public class SubscriptionHistoryRequest
{
    public int SubscriptionId { get; set; }
}

public class SubscriptionRevisionRequest
{
    public int SubscriptionId { get; set; }
    public int Number { get; set; }
}

/// <summary>One save in a subscription's history, and which settings it changed.</summary>
public record SubscriptionRevisionRow(int Number, System.DateTime SavedOn, string SavedBy, IReadOnlyList<string> Changed);

/// <summary>
/// A subscription's revisions, newest first: who saved it, when, and which settings each save
/// changed from the one before. Recorded from the first save after history existed.
/// </summary>
[HandlerName("history")]
public class History(BitweenDbContext dbContext, RequestContext requestContext)
    : IQueryHandler<SubscriptionHistoryRequest, object>
{
    public async Task<object> Handle(SubscriptionHistoryRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);

        var revisions = await dbContext.Set<SubscriptionRevision>().AsNoTracking()
            .Where(r => r.SubscriptionId == request.SubscriptionId)
            .OrderBy(r => r.Number)
            .ToListAsync();

        var names = await AccountNames(revisions.Select(r => r.SavedBy));
        var rows = new List<SubscriptionRevisionRow>();
        JObject previous = null;
        foreach (var revision in revisions)
        {
            var snapshot = JObject.Parse(revision.Snapshot);
            var changed = previous == null
                ? new List<string>()
                : snapshot.Properties().Select(p => p.Name)
                    .Union(previous.Properties().Select(p => p.Name))
                    .Where(name => !JToken.DeepEquals(snapshot[name], previous[name]))
                    .OrderBy(name => name)
                    .ToList();
            rows.Add(new SubscriptionRevisionRow(revision.Number, revision.SavedOn,
                names.GetValueOrDefault(revision.SavedBy ?? "", revision.SavedBy), changed));
            previous = snapshot;
        }

        rows.Reverse();
        return rows;
    }

    /// <summary>Accounts by their id, as revisions record them, to the name they're shown by.</summary>
    async Task<Dictionary<string, string>> AccountNames(IEnumerable<string> ids)
    {
        var numeric = ids.Distinct().Select(id => int.TryParse(id, out var n) ? n : (int?)null)
            .Where(n => n != null).Select(n => n.Value).ToList();
        return await dbContext.Set<Domain.Accounts.Account>().AsNoTracking()
            .Where(a => numeric.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id.ToString(), a => a.DisplayName);
    }
}

/// <summary>
/// One revision's configuration, in the shape the subscription itself is read in. Restoring it is
/// saving it as the subscription, which the admin UI does, so every check a save makes applies.
/// </summary>
[HandlerName("revision")]
public class Revision(BitweenDbContext dbContext, RequestContext requestContext)
    : IQueryHandler<SubscriptionRevisionRequest, object>
{
    public async Task<object> Handle(SubscriptionRevisionRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);

        var revision = await dbContext.Set<SubscriptionRevision>().AsNoTracking()
                           .FirstOrDefaultAsync(r => r.SubscriptionId == request.SubscriptionId && r.Number == request.Number)
                       ?? throw new SWNotFoundException($"{request.SubscriptionId}/{request.Number}");
        return new { revision.Number, revision.SavedOn, Configuration = JObject.Parse(revision.Snapshot) };
    }
}
