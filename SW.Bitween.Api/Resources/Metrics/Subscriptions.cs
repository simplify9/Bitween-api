using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using SW.Bitween.Model;
using SW.Bitween.Services.Metrics;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Metrics;

/// <summary>Every subscription's health over the window: status, success, timing, retries, traffic by day.</summary>
[HandlerName("subscriptions")]
public class Subscriptions(BitweenDbContext db, RequestContext requestContext, IMemoryCache cache)
    : IQueryHandler<MetricsWindowRequest, object>
{
    public async Task<object> Handle(MetricsWindowRequest request)
    {
        await requestContext.EnsurePermission(db, Model.Permissions.Subscriptions.View);
        var days = MetricsQueries.Days(request?.Days);
        return await cache.Cached($"subscriptions.{days}", async () =>
        {
            var rows = await SubscriptionHealth.ComputeAsync(db, days, DateTime.UtcNow);
            return (object)new
            {
                Since = await MetricsQueries.SinceAsync(db),
                Days = days,
                Subscriptions = rows.Select(r => new
                {
                    r.SubscriptionId, r.Name, r.Type, r.Status, r.SuccessRate, r.P95Ms, r.LastExchangeHour,
                    r.QuietHours, r.UsualGapHours, r.Daily,
                    r.Totals.Total, r.Totals.Failed, r.Totals.BadResponse, r.Totals.Retries, r.Totals.ManualRetries,
                    Bytes = r.Totals.BytesIn + r.Totals.BytesOut,
                }),
            };
        });
    }
}
