using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Dashboard;

[HandlerName("ChartsDataPoints")]
public class ChartsDataPoints : IQueryHandler<object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;
    private readonly DateTime _dataDateLimit;

    public ChartsDataPoints(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
        _dataDateLimit = DateTime.UtcNow.AddMonths(-3);
    }

    public async Task<object> Handle()
    {
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Dashboard.View);

        var xChangesPerDay = await _dbContext.Set<Xchange>()
            .AsNoTracking()
            .Where(i => i.StartedOn >= _dataDateLimit)
            .GroupBy(i => i.StartedOn.Date)
            .OrderBy(i => i.Key)
            .Select(i => new
            {
                DateTime = i.Key.ToString("MMM dd"),
                Count = i.Count()
            }).ToListAsync();

        var subscriptionsUsageCount = await _dbContext.Set<Xchange>()
            .AsNoTracking()
            .Where(i => i.SubscriptionId != null)
            .Where(i => i.StartedOn >= _dataDateLimit)
            .GroupBy(i => i.SubscriptionId)
            .OrderBy(i => i.Key)
            .Select(i => new
            {
                SubscriptionId = i.Key,
                Count = i.Count()
            })
            .OrderByDescending(i => i.Count)
            .ToListAsync();

        return new
        {
            subscriptionsUsageCount,
            xChangesPerDay,
            LastUpdated = DateTime.UtcNow
        };
    }
}