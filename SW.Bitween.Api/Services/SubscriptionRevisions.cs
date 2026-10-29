using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using SW.Bitween.Domain;
using SW.Bitween.JsonConverters;
using Microsoft.Extensions.Logging;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>
/// A subscription's history: a revision recorded after each save that changed its configuration.
/// </summary>
/// <remarks>
/// Restoring is not done here. It is an ordinary save of the revision's configuration, made by the
/// caller, so it is checked exactly as any other save is and becomes a revision of its own.
/// </remarks>
public class SubscriptionRevisions(BitweenDbContext dbContext, AdapterStartupValues startupValues, RequestContext requestContext,
    ILogger<SubscriptionRevisions> logger)
{
    /// <summary>Revisions kept per subscription; older ones are removed as new ones are recorded.</summary>
    public const int Keep = 50;

    /// <summary>
    /// What the API answers that isn't configuration: running state, and fields fixed at creation
    /// that a save can't change. Left out of revisions, so a pause or a failure count never shows
    /// as a change, and restoring never touches them.
    /// </summary>
    static readonly HashSet<string> NotConfiguration = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "type", "documentId", "partnerId", "aggregationForId", "inactive", "temporary",
        "receiveOn", "aggregateOn", "consecutiveFailures", "lastException", "pausedOn", "pausedAutomatically",
        "quietSince", "categoryCode", "categoryDescription", "newResponseSubscription",
    };

    /// <summary>The API's own JSON settings, so a snapshot reads back as the API would answer it.</summary>
    public static readonly JsonSerializer Json = JsonSerializer.Create(new JsonSerializerSettings
    {
        Converters =
        {
            new PropertyMatchSpecificationJsonConverter(), new MatcherJsonConverter(), new DelayStrategyJsonConverter(),
            new Newtonsoft.Json.Converters.StringEnumConverter(),
        },
        ContractResolver = new CamelCasePropertyNamesContractResolver
        {
            NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false },
        },
    });

    /// <summary>
    /// Records the subscription's configuration as it is now, unless it is what the last revision
    /// holds. Called after a save has committed, so a failure here is logged rather than failing it.
    /// </summary>
    public async Task RecordAsync(int subscriptionId)
    {
        try
        {
            await RecordCore(subscriptionId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Subscription {SubscriptionId} was saved, but its revision could not be recorded.", subscriptionId);
        }
    }

    /// <summary>
    /// Stricter than reading a subscription: a value stays only when its adapter says the setting
    /// isn't secret. One the adapter doesn't describe - an adapter that can't be described, a key
    /// it no longer has - is kept as the sentinel, since a revision is stored, and stored as plain
    /// JSON however the subscription's own columns are protected. Restoring the sentinel keeps the
    /// value the subscription has, so all a revision loses by it is showing that value's history.
    /// </summary>
    async Task<ICollection<KeyAndValue>> OnlyWhatIsNotSecret(string adapterId, ICollection<KeyAndValue> properties)
    {
        if (properties == null || properties.Count == 0) return properties;

        IDictionary<string, StartupValue> described;
        try
        {
            described = string.IsNullOrEmpty(adapterId) ? new Dictionary<string, StartupValue>() : await startupValues.Describe(adapterId);
        }
        catch
        {
            described = new Dictionary<string, StartupValue>();
        }

        return properties.Select(p =>
            string.IsNullOrEmpty(p.Value) || (described.TryGetValue(p.Key, out var value) && !value.Private)
                ? p
                : new KeyAndValue { Key = p.Key, Value = Sentinel }).ToList();
    }

    const string Sentinel = "__private__";

    async Task RecordCore(int subscriptionId)
    {
        var current = await new Resources.Subscriptions.Get(dbContext, startupValues, requestContext).Read(subscriptionId);
        current.HandlerProperties = await OnlyWhatIsNotSecret(current.HandlerId, current.HandlerProperties);
        current.MapperProperties = await OnlyWhatIsNotSecret(current.MapperId, current.MapperProperties);
        current.ReceiverProperties = await OnlyWhatIsNotSecret(current.ReceiverId, current.ReceiverProperties);
        current.ValidatorProperties = await OnlyWhatIsNotSecret(current.ValidatorId, current.ValidatorProperties);
        var configuration = JObject.FromObject(current, Json);
        foreach (var name in configuration.Properties().Select(p => p.Name).Where(NotConfiguration.Contains).ToList())
            configuration.Remove(name);
        var snapshot = configuration.ToString(Formatting.None);

        var last = await dbContext.Set<SubscriptionRevision>().AsNoTracking()
            .Where(r => r.SubscriptionId == subscriptionId)
            .OrderByDescending(r => r.Number)
            .Select(r => new { r.Number, r.Snapshot })
            .FirstOrDefaultAsync();
        if (last != null && JToken.DeepEquals(JToken.Parse(last.Snapshot), configuration)) return;

        dbContext.Add(new SubscriptionRevision(subscriptionId, (last?.Number ?? 0) + 1, snapshot,
            requestContext.GetNameIdentifier()));
        await dbContext.SaveChangesAsync();

        var cutoff = (last?.Number ?? 0) + 1 - Keep;
        if (cutoff > 0)
            await dbContext.Set<SubscriptionRevision>()
                .Where(r => r.SubscriptionId == subscriptionId && r.Number <= cutoff)
                .ExecuteDeleteAsync();
    }
}
