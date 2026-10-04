using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Dashboard;

[HandlerName("XChangesAndSubscriptionsInfo")]
public class XChangesAndSubscriptionsInfo(BitweenDbContext dbContext, XchangeService xchangeService,
    RequestContext requestContext) : IQueryHandler<object>
{
    private readonly DateTime _dataDateLimit = DateTime.UtcNow.AddMonths(-3);

    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Dashboard.View);

        var totalXchangesCount = await dbContext.Set<Xchange>().AsNoTracking().CountAsync();
        var xChangeCountInTimeframe = await dbContext.Set<Xchange>()
            .Where(i => i.StartedOn >= _dataDateLimit)
            .AsNoTracking().CountAsync();

        var xchangeResultBase = dbContext.Set<XchangeResult>().AsNoTracking().AsQueryable();

        var badResponseXchanges = await xchangeResultBase
            .Where(i => i.FinishedOn >= _dataDateLimit)
            .Where(i => i.ResponseBad).CountAsync();

        var failedXchanges = await xchangeResultBase
            .Where(i => i.FinishedOn >= _dataDateLimit)
            .Where(i => !string.IsNullOrEmpty(i.Exception)).CountAsync();

        var latestFailedQ = from xchange in dbContext.Set<Xchange>()
            join result in dbContext.Set<XchangeResult>() on xchange.Id equals result.Id into xr
            from result in xr.DefaultIfEmpty()
            join subscriber in dbContext.Set<Subscription>() on xchange.SubscriptionId equals subscriber.Id into xs
            from subscriber in xs.DefaultIfEmpty()
            select new
            {
                SubscriptionName = subscriber.Name,
                result.FinishedOn,
                result.ResponseBad,
                result.Exception,
                ResponseFileKey = xchangeService.FileKey(xchange.Id, xchange.FilesPrefix, result.ResponseSize, XchangeFileType.Response),
            };

        var latestFailedxCahanges = await latestFailedQ
            .Where(i => i.ResponseBad || !string.IsNullOrEmpty(i.Exception))
            .OrderByDescending(i => i.FinishedOn)
            .Take(20)
            .AsNoTracking()
            .AsSplitQuery()
            .ToListAsync();

        var successfulXchanges = await xchangeResultBase
            .Where(i => i.FinishedOn >= _dataDateLimit)
            .Where(i => string.IsNullOrEmpty(i.Exception))
            .Where(i => !i.OutputBad)
            .Where(i => !i.ResponseBad)
            .CountAsync();

        var res = new
        {
            successfulXchanges,
            latestFailedxCahanges,
            failedXchanges,
            badResponseXchanges,
            totalXchangesCount,
            xChangeCountInTimeframe,
            LastUpdated = DateTime.UtcNow
        };
        //_memoryCache.Set("CACHE_KEY", res, TimeSpan.FromMinutes(1));

        return res;
    }
}