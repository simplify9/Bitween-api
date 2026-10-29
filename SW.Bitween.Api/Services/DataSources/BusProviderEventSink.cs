using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Domain.Gateway;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;
using SW.Serverless.Resident;
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace SW.Bitween.Services.DataSources;

/// <summary>
/// Where a message from an external broker enters Bitween.
///
/// The adapter owns the connection and hands the payload over; everything after that is the path
/// an internally-bussed message already takes - <see cref="XchangeService.SubmitFilterXchange"/>,
/// which persists the Xchange, writes the payload to cloud storage and lets the filter decide
/// which subscriptions run. Ingress from a broker and ingress from the API are the same thing
/// past this point, deliberately.
///
/// The adapter does NOT acknowledge its broker until this returns Accepted. That ordering is the
/// whole contract: persist, then ack. A crash in between means redelivery, which is why every
/// event carries a dedupe key.
/// </summary>
public class BusProviderEventSink(IServiceProvider serviceProvider, ILogger<BusProviderEventSink> logger)
    : IAdapterEventSink
{
    public async Task<EventOutcome> OnEventAsync(InboundEvent inboundEvent, CancellationToken cancellationToken)
    {
        // A resident adapter is a singleton and outlives any request, so ingest gets its own scope
        // per message rather than borrowing one.
        using var scope = serviceProvider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xchangeService = scope.ServiceProvider.GetRequiredService<XchangeService>();

        if (!int.TryParse(inboundEvent.InstanceKey, out var dataSourceId))
            return EventOutcome.Rejected($"'{inboundEvent.InstanceKey}' is not a data source id.");

        // Which gateway on this data source owns this endpoint. Endpoint is what the adapter was
        // told to consume - a queue, a topic, an SQS URL.
        var gateway = await dbContext.Set<BusGateway>()
            .Where(g => g.DataSourceId == dataSourceId && !g.Inactive)
            .Where(g => g.Endpoint == inboundEvent.Endpoint || g.Endpoint == null)
            // An exact endpoint match beats the catch-all. Ordering by the endpoint itself does
            // NOT express that: PostgreSQL sorts NULLS FIRST on a descending order, so the
            // catch-all would win every race and every specific endpoint's messages would be
            // filed against the wrong Document. Order by the match itself instead - true first,
            // and it means the same thing on all three providers.
            .OrderByDescending(g => g.Endpoint == inboundEvent.Endpoint)
            .FirstOrDefaultAsync(cancellationToken);

        if (gateway == null)
        {
            // Not an error: the adapter is consuming something no gateway claims. Rejecting would
            // requeue it forever, so accept and drop with a warning instead.
            logger.LogWarning(
                "No active bus gateway on data source {DataSourceId} claims endpoint '{Endpoint}'. Discarding.",
                dataSourceId, inboundEvent.Endpoint);
            return EventOutcome.Ok("unclaimed");
        }

        var dataSource = await dbContext.Set<DataSource>().AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == dataSourceId, cancellationToken);

        var dedupeKey = BuildDedupeKey(dataSourceId, inboundEvent.DedupeKey);
        var deduplicating = dedupeKey != null && (dataSource?.DeduplicationWindowDays ?? 0) > 0;

        if (deduplicating)
        {
            // Added to the SAME DbContext the Xchange will be written through, so both commit in
            // one SaveChanges. Splitting them would give two failure modes, and the worse one is
            // silent: the dedupe row committing while the Xchange fails suppresses that message
            // for ever.
            dbContext.Add(new InboundMessage(dedupeKey!, dataSourceId, xchangeId: null));
        }

        try
        {
            var payload = Encoding.UTF8.GetString(inboundEvent.Payload ?? Array.Empty<byte>());
            var file = new XchangeFile(payload);

            // Same entry point the internal bus uses. Filtering, mapping, handling, auto-retry and
            // the audit trail all follow from here unchanged.
            await xchangeService.SubmitFilterXchange(
                gateway.DocumentId,
                file,
                references: string.IsNullOrEmpty(inboundEvent.DedupeKey)
                    ? null
                    : new[] { inboundEvent.DedupeKey },
                correlationId: inboundEvent.Traceparent);

            return EventOutcome.Ok(inboundEvent.DedupeKey);
        }
        catch (DbUpdateException ex) when (deduplicating && IsUniqueViolation(ex))
        {
            // The insert lost the race, so this message has already been persisted. ACCEPT it:
            // rejecting would nack and redeliver a message that is by definition already handled,
            // and the queue would never drain.
            logger.LogInformation(
                "Duplicate message on data source {DataSourceId} endpoint {Endpoint} (key {Key}); already persisted.",
                dataSourceId, inboundEvent.Endpoint, dedupeKey);

            return EventOutcome.Ok(inboundEvent.DedupeKey);
        }
        catch (Exception ex)
        {
            // Rejecting is the right answer: the adapter nacks, the broker redelivers, and nothing
            // is silently lost because Bitween happened to be unhealthy for a moment.
            logger.LogError(ex, "Failed to ingest a message from data source {DataSourceId} endpoint {Endpoint}.",
                dataSourceId, inboundEvent.Endpoint);
            return EventOutcome.Rejected(ex.Message);
        }
    }

    /// <summary>
    /// Namespaced by data source, always. The adapters already qualify their keys by host and
    /// queue, but an SP-API key is just the notification id - globally unique, so two data sources
    /// subscribed to the same notification would silently deduplicate against each other. That
    /// might occasionally be wanted; it should never be something you get by accident.
    /// </summary>
    private static string BuildDedupeKey(int dataSourceId, string adapterKey) =>
        string.IsNullOrWhiteSpace(adapterKey) ? null : $"{dataSourceId}:{adapterKey}";

    /// <summary>
    /// Bitween runs on three providers, and each reports a unique-constraint violation its own
    /// way: PostgreSQL 23505, MySQL 1062, SQL Server 2601 and 2627.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (var inner = exception.InnerException; inner != null; inner = inner.InnerException)
        {
            var state = inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string;
            if (state == "23505") return true;

            var number = inner.GetType().GetProperty("Number")?.GetValue(inner);
            if (number is int code && code is 1062 or 2601 or 2627) return true;

            if (inner.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
                inner.Message.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
