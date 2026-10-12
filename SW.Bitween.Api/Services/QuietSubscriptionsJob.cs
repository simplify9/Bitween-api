using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.Bitween.Services.Adapters;
using SW.Scheduler;

namespace SW.Bitween;

/// <summary>
/// Finds subscriptions whose <see cref="ExpectTraffic"/> went unmet - a partner that stops sending
/// fails nothing, so nothing else would say so - and tells the notifiers that watch them for failed
/// results. Once when it goes quiet, once when exchanges arrive again.
/// <para>
/// Every question asked of the exchanges is bounded in time: has one started since a moment no
/// further back than the expected gap (or an hour, for the way back). Both the subscription and the
/// start time are indexed, so neither depends on how much history the instance keeps.
/// </para>
/// </summary>
[ScheduleConfig(AllowConcurrentExecution = false, MisfireInstructions = MisfireInstructions.Skip)]
public class QuietSubscriptionsJob(BitweenDbContext db, IAdapterInvoker adapterInvoker, ILogger<QuietSubscriptionsJob> logger)
    : IScheduledJob
{
    public const string GoneQuiet = "gone-quiet";
    public const string Back = "back";

    // How far back an alert looks for the last exchange, to say when traffic stopped.
    static readonly TimeSpan LastSeenLookback = TimeSpan.FromDays(7);

    // Exchanges since QuietSince are looked for in at least this much of the recent past. A job that
    // runs every few minutes sees each one long before it falls out; one older than this followed by
    // silence means the subscription is quiet again anyway, so it stays flagged.
    static readonly TimeSpan BackLookback = TimeSpan.FromHours(1);

    public Task Execute() => Run(DateTime.UtcNow);

    /// <summary>One pass as at <paramref name="utcNow"/>.</summary>
    public async Task Run(DateTime utcNow)
    {
        // Few rows and narrow, so filtered here rather than by comparing a JSON column in SQL.
        var subscriptions = (await db.Set<Subscription>().AsNoTracking()
                .Select(s => new { s.Id, s.Name, s.DocumentId, s.Inactive, s.PausedOn, s.ExpectTraffic, s.QuietSince })
                .ToListAsync())
            .Where(s => s.QuietSince != null || (s.ExpectTraffic != null && !s.Inactive && s.PausedOn == null))
            .ToList();

        foreach (var s in subscriptions)
        {
            try
            {
                TrafficWindow window = null;
                if (s.ExpectTraffic != null)
                {
                    try
                    {
                        window = TrafficWindow.Parse(s.ExpectTraffic);
                    }
                    catch (SW.PrimitiveTypes.SWValidationException e)
                    {
                        // Saved valid, so most likely a time zone this host lacks.
                        logger.LogWarning("Subscription {SubscriptionId} expects traffic in a way that can't be checked here: {Reason}",
                            s.Id, e.Message);
                    }
                }

                if (s.QuietSince is { } quietSince)
                {
                    var lookback = window?.Every > BackLookback ? window.Every : BackLookback;
                    var since = quietSince > utcNow - lookback ? quietSince : utcNow - lookback;
                    var first = await db.Set<Xchange>().AsNoTracking()
                        .Where(x => x.SubscriptionId == s.Id && x.StartedOn > since)
                        .MinAsync(x => (DateTime?)x.StartedOn);
                    // Cleared outside the window too: traffic is back whenever it comes back.
                    if (first != null && await SetQuietSince(s.Id, quietSince, null))
                    {
                        var stoppedAt = await LastStartedBefore(s.Id, quietSince) ?? quietSince;
                        await Notify(s.Id, s.Name, s.DocumentId, Back,
                            $"Exchanges arriving again after {Duration(first.Value - stoppedAt)}.", utcNow);
                    }
                    continue;
                }

                if (window?.QuietUnlessAfter(utcNow) is not { } cutoff) continue;
                var arrived = await db.Set<Xchange>().AsNoTracking()
                    .AnyAsync(x => x.SubscriptionId == s.Id && x.StartedOn > cutoff);
                if (arrived || !await SetQuietSince(s.Id, null, utcNow)) continue;

                var last = await LastStartedBefore(s.Id, utcNow);
                var silence = last == null
                    ? $"No exchanges in the last {LastSeenLookback.TotalDays:0} days"
                    : $"No exchanges since {last.Value:yyyy-MM-dd HH:mm} UTC";
                await Notify(s.Id, s.Name, s.DocumentId, GoneQuiet,
                    $"{silence}; {s.Name} expects at least one {window.Describe()}.", utcNow);
            }
            catch (Exception e)
            {
                // One subscription's trouble mustn't keep the rest from being checked.
                logger.LogError(e, "Checking subscription {SubscriptionId} for missing traffic failed.", s.Id);
            }
        }
    }

    /// <summary>
    /// Moves QuietSince from <paramref name="expected"/> to <paramref name="value"/>, and says whether
    /// this pass did it. Conditional, so an alert goes out once even if two passes overlap.
    /// </summary>
    async Task<bool> SetQuietSince(int subscriptionId, DateTime? expected, DateTime? value) =>
        await db.Set<Subscription>()
            .Where(x => x.Id == subscriptionId && x.QuietSince == expected)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.QuietSince, value)) == 1;

    Task<DateTime?> LastStartedBefore(int subscriptionId, DateTime at)
    {
        var from = at - LastSeenLookback;
        return db.Set<Xchange>().AsNoTracking()
            .Where(x => x.SubscriptionId == subscriptionId && x.StartedOn <= at && x.StartedOn > from)
            .MaxAsync(x => (DateTime?)x.StartedOn);
    }

    /// <summary>
    /// The notifiers watching the subscription for failed results get the same payload an exchange
    /// result sends. No XchangeNotification row: there's no exchange for it to belong to.
    /// </summary>
    async Task Notify(int subscriptionId, string subscriptionName, int documentId, string kind, string sentence, DateTime utcNow)
    {
        logger.LogWarning("Subscription {SubscriptionId} ({SubscriptionName}) {Event}: {Detail}",
            subscriptionId, subscriptionName, kind, sentence);

        var notifiers = (await db.Set<Notifier>().AsNoTracking()
                .Where(n => !n.Inactive && n.RunOnFailedResult && n.HandlerId != null)
                .ToListAsync())
            .Where(n => n.RunOnSubscriptions?.Contains(subscriptionId) == true)
            .ToList();
        if (notifiers.Count == 0) return;

        var documentName = await db.Set<Document>().AsNoTracking()
            .Where(d => d.Id == documentId).Select(d => d.Name).FirstOrDefaultAsync();
        // Stands in for the exchange id, so a handler that files or threads by it has one.
        var alertId = $"{kind}-{subscriptionId}-{utcNow:yyyyMMddHHmmss}";
        var notification = new XchangeResultNotification
        {
            Id = alertId,
            Success = kind == Back,
            Exception = sentence,
            StartedOn = utcNow,
            FinishedOn = utcNow,
            SubscriptionId = subscriptionId,
            SubscriptionName = subscriptionName,
            DocumentId = documentId,
            DocumentName = documentName,
            CorrelationId = alertId,
            Event = kind,
        };

        foreach (var notifier in notifiers)
        {
            var error = await NotifierDelivery.SendAsync(adapterInvoker, notifier, notification, null, alertId);
            if (error == null)
                logger.LogInformation("Told notifier {NotifierId} ({NotifierName}) subscription {SubscriptionId} {Event}.",
                    notifier.Id, notifier.Name, subscriptionId, kind);
            else
                logger.LogError(error, "Notifier {NotifierId} ({NotifierName}) could not be told subscription {SubscriptionId} {Event}.",
                    notifier.Id, notifier.Name, subscriptionId, kind);
        }
    }

    static string Duration(TimeSpan span)
    {
        var minutes = (int)Math.Max(1, Math.Round(span.TotalMinutes));
        if (minutes < 120) return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        var hours = (int)Math.Round(span.TotalHours);
        if (hours < 48) return $"{hours} hours";
        return $"{(int)Math.Round(span.TotalDays)} days";
    }
}
