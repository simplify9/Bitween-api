using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Dashboard;

[HandlerName("ChartsDataPoints")]
public class ChartsDataPoints : IQueryHandler
{
    private readonly InfolinkDbContext _dbContext;
    private readonly DateTime _dataDateLimit;

    public ChartsDataPoints(InfolinkDbContext dbContext)
    {
        _dbContext = dbContext;
        _dataDateLimit = DateTime.UtcNow.AddMonths(-3);
    }

    public async Task<object> Handle()
    {
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
            }).ToListAsync();

        return new
        {
            subscriptionsUsageCount,
            xChangesPerDay,
            LastUpdated = DateTime.UtcNow
        };
    }
}