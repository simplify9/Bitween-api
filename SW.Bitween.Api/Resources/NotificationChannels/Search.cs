using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.NotificationChannels;

public class Search(BitweenDbContext dbContext, RequestContext requestContext) : ISearchyHandler
{
    public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
    {
        var query = dbContext.Set<NotificationChannel>().AsNoTracking()
            .Select(c => new NotificationChannelRow { Id = c.Id, Name = c.Name, HandlerId = c.HandlerId });

        // Lookup returns only id/name pairs: every page that picks a channel — a subscription's
        // notifications, a retry policy's alerts — needs the names, whoever is editing it.
        if (lookup)
            return await query.Search(searchyRequest.Conditions)
                .ToDictionaryAsync(k => k.Id.ToString(), v => v.Name);

        await requestContext.EnsurePermission(dbContext, Model.Permissions.Notifiers.View);

        var rows = await query.OrderBy(c => c.Name)
            .Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex)
            .ToListAsync();

        var uses = await NotificationChannelUsage.Find(dbContext);
        foreach (var row in rows)
            row.UsedBy = uses.GetValueOrDefault(row.Id) ?? [];

        return new SearchyResponse<NotificationChannelRow>
        {
            TotalCount = await query.Search(searchyRequest.Conditions).CountAsync(),
            Result = rows
        };
    }
}
