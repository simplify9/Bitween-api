using System;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Retention;

/// <summary>
/// Whether retention would remove exchanges before an aggregation with these schedules collects them —
/// shown on the aggregation as its schedule is edited, before it's saved. Changes nothing.
/// </summary>
[HandlerName("aggregation")]
public class Aggregation(BitweenDbContext dbContext, RequestContext requestContext, BitweenOptions options)
    : ICommandHandler<AggregationRetentionRequest, object>
{
    public async Task<object> Handle(AggregationRetentionRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);

        var days = options.ExchangeRetentionDays;
        var schedules = (request.Schedules ?? [])
            .Select(s => new Schedule(s.Recurrence, new TimeSpan(s.Days, s.Hours, s.Minutes, 0), s.Backwards))
            .ToList();

        return new AggregationRetentionCheck
        {
            Warning = AggregationRetention.MissedDays(schedules, days, DateTime.UtcNow) is { } apart
                ? $"This aggregation can go up to {apart} days between runs, but exchanges are removed after " +
                  $"{days} days (Settings → Documents & storage). Exchanges removed in between are never rolled up."
                : null
        };
    }
}
