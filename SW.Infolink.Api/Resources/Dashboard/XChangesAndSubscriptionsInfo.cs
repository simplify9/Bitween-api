using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Dashboard;

[HandlerName("XChangesAndSubscriptionsInfo")]
public class XChangesAndSubscriptionsInfo : IQueryHandler
{
    private readonly InfolinkDbContext _dbContext;

    private readonly DateTime _dataDateLimit;
    private readonly XchangeService _xchangeService;

    // private readonly IMemoryCache _memoryCache;
    // private const string CACHE_KEY = "XChangesAndSubscriptionsInfoCache";

    public XChangesAndSubscriptionsInfo(InfolinkDbContext dbContext, XchangeService xchangeService)
    {
        _dbContext = dbContext;
        _xchangeService = xchangeService;
        //_memoryCache = memoryCache;
        _dataDateLimit = DateTime.UtcNow.AddMonths(-3);
    }

    public async Task<object> Handle()
    {
        var totalXchangesCount = await _dbContext.Set<Xchange>().AsNoTracking().CountAsync();
        var xChangeCountInTimeframe = await _dbContext.Set<Xchange>()
            .Where(i => i.StartedOn >= _dataDateLimit)
            .AsNoTracking().CountAsync();

        var xchangeResultBase = _dbContext.Set<XchangeResult>().AsNoTracking().AsQueryable();

        var badResponseXchanges = await xchangeResultBase
            .Where(i => i.FinishedOn >= _dataDateLimit)
            .Where(i => i.ResponseBad).CountAsync();

        var failedXchanges = await xchangeResultBase
            .Where(i => i.FinishedOn >= _dataDateLimit)
            .Where(i => !string.IsNullOrEmpty(i.Exception)).CountAsync();


        var latestFailedQ = from xchange in _dbContext.Set<Xchange>()
            join result in _dbContext.Set<XchangeResult>() on xchange.Id equals result.Id into xr
            from result in xr.DefaultIfEmpty()
            join subscriber in _dbContext.Set<Subscription>() on xchange.SubscriptionId equals subscriber.Id into xs
            from subscriber in xs.DefaultIfEmpty()
            select new
            {
                SubscriptionName = subscriber.Name,
                result.FinishedOn,
                result.ResponseBad,
                result.Exception,
                ResponseFileKey = _xchangeService.GetFileKey(xchange.Id, result.ResponseSize, XchangeFileType.Response),
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