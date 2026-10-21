using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Xchanges;

/// <summary>
/// The exchange filters that cannot be handed to Searchy as-is, because they do not correspond to
/// one column: an id that should also match relatives, a status assembled from two columns and the
/// absence of a result row, a promoted-property substring that has to be case-folded.
/// </summary>
/// <remarks>
/// Shared by the search and by bulk retry, which needs "every exchange this filter matches" to mean
/// exactly the set the person was looking at when they chose it. The two drifting apart would be
/// invisible until a bulk retry quietly acted on a different set of exchanges than the one on
/// screen.
/// </remarks>
internal static class XchangeFilters
{
    /// <summary>
    /// The exchanges a filter matches, filtered exactly as the search would have filtered them, as
    /// lean rows: the columns the list can filter on and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What a "select all matching" bulk action and the summary both start from. Dates are moved to
    /// UTC and a run filter is resolved here, inside the one entry point, so neither caller can
    /// forget a step and select a different set than the list showed for the same filter.
    /// </para>
    /// <para>
    /// The projection carries the columns the exchange list can filter on (see
    /// <c>buildExchangeQuery</c> in the client) and nothing else, so this stays translatable and
    /// composable - the search's own projection cannot be reused for that, as it builds file URLs
    /// in C#. Filtering on a column that is not here would silently match nothing, so a new filter
    /// on the list needs a column here too.
    /// </para>
    /// <para>
    /// The special filters are removed from <paramref name="searchyRequest"/> as they are applied,
    /// as they are for the search; what is left in it afterwards is only what Searchy handled.
    /// </para>
    /// </remarks>
    internal static async Task<IQueryable<XchangeRow>> FilteredRowsAsync(BitweenDbContext dbContext,
        SearchyRequest searchyRequest)
    {
        searchyRequest.DatesToUtc();
        // Async, and inside here rather than in the callers, so a "select all matching" over a run
        // selects exactly the rows the list showed for it.
        await ResolveReceiveAttemptFilterAsync(searchyRequest, dbContext);

        var query = from xchange in dbContext.Set<Xchange>()
                    join result in dbContext.Set<XchangeResult>() on xchange.Id equals result.Id into xr
                    from result in xr.DefaultIfEmpty()
                    join agg in dbContext.Set<XchangeAggregation>() on xchange.Id equals agg.Id into xa
                    from agg in xa.DefaultIfEmpty()
                    join promoted in dbContext.Set<XchangePromotedProperties>() on xchange.Id equals promoted.Id into xp
                    from promoted in xp.DefaultIfEmpty()
                    join subscriber in dbContext.Set<Subscription>() on xchange.SubscriptionId equals subscriber.Id into xs
                    from subscriber in xs.DefaultIfEmpty()
                    select new XchangeRow
                    {
                        Id = xchange.Id,
                        SubscriptionId = xchange.SubscriptionId,
                        DocumentId = xchange.DocumentId,
                        StartedOn = xchange.StartedOn,
                        CorrelationId = xchange.CorrelationId,
                        Status = result.Success,
                        ResponseBad = result.ResponseBad,
                        // For the ErrorContains filter. Only read when that filter is set, so it
                        // costs nothing to the selections that do not use it.
                        Exception = result.Exception,
                        RetryFor = xchange.RetryFor,
                        AggregationXchangeId = agg.AggregationXchangeId,
                        PromotedPropertiesRaw = promoted.PropertiesRaw,
                        // Same fallback the search makes for exchanges written before the column
                        // existed, so a partner filter selects the same rows it listed.
                        PartnerId = xchange.PartnerId ?? subscriber.PartnerId
                    };

        query = query.ApplySpecialFilters(searchyRequest, dbContext);
        return query.AsNoTracking().Search(searchyRequest.Conditions);
    }

    /// <summary>
    /// Turns a <c>ReceiveAttemptId</c> filter into the <c>Id</c> filter the rest of the pipeline
    /// already understands, by reading the run's own record of the exchanges it created.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ApplySpecialFilters"/>, and async, because the run has to be read
    /// before the query can be built. <c>ReceiveAttempt.ExchangeIds</c> is persisted as a single
    /// separator-delimited string rather than an array (see <c>BitweenDbContext</c>), so no
    /// subquery can reach inside it - and with three database providers in the solution there is
    /// no one translation that would.
    ///
    /// It rewrites rather than filters here so the result stays identical to the long
    /// <c>?ids=a,b,c</c> URL this replaces, relatives and all - the only thing that changes is that
    /// the ids no longer have to fit in a web address.
    /// </remarks>
    internal static async Task ResolveReceiveAttemptFilterAsync(SearchyRequest searchyRequest,
        BitweenDbContext dbContext)
    {
        var condition = searchyRequest.Conditions.FirstOrDefault();
        if (condition == null)
            return;

        var attemptFilters = condition.Filters.Where(f => f.Field == "ReceiveAttemptId").ToList();
        foreach (var attemptFilter in attemptFilters)
        {
            if (!int.TryParse(attemptFilter.Value?.ToString(), out var attemptId))
                throw new SWValidationException("NOT_SUPPORTED",
                    $"'{attemptFilter.Value}' is not a receive attempt id.");

            var exchangeIds = await dbContext.Set<ReceiveAttempt>().AsNoTracking()
                .Where(a => a.Id == attemptId)
                .Select(a => a.ExchangeIds)
                .SingleOrDefaultAsync();

            if (exchangeIds == null)
                throw new SWValidationException("NOT_FOUND",
                    $"Run {attemptId} no longer exists. Its history may have been cleaned up.");

            // A run that created nothing leaves an empty array, and the Contains branch below reads
            // that as "matches no row" - which is the honest answer. Dropping the filter instead
            // would widen the selection to every exchange, and bulk retry acts on whatever this
            // selects.
            condition.Filters.Add(new SearchyFilter
            {
                Field = "Id",
                Rule = SearchyRule.Contains,
                ValueStringArray = exchangeIds,
            });
            condition.Filters.Remove(attemptFilter);
        }
    }

    /// <summary>
    /// Applies the special filters and removes them from <paramref name="searchyRequest"/>, leaving
    /// the plain per-column ones for Searchy to handle.
    /// </summary>
    internal static IQueryable<XchangeRow> ApplySpecialFilters(this IQueryable<XchangeRow> query,
        SearchyRequest searchyRequest, BitweenDbContext dbContext)
    {
        var condition = searchyRequest.Conditions.FirstOrDefault();
        if (condition == null)
            return query;

        var idFilters = condition.Filters.Where(f => f.Field == "Id").ToList();
        foreach (var idFilter in idFilters)
        {
            var value = idFilter.Value.ToString();
            switch (idFilter.Rule)
            {
                case SearchyRule.EqualsTo:
                    query = query.Where(i =>
                        i.Id == value || i.RetryFor == value || i.AggregationXchangeId == value);
                    break;
                case SearchyRule.Contains:
                    {
                        var valueAsArray = idFilter.ValueStringArray;
                        query = query.Where(i =>
                            valueAsArray.Any(v => i.RetryFor == v) ||
                            valueAsArray.Any(v => i.AggregationXchangeId == v) ||
                            valueAsArray.Any(v => i.Id == v)
                        );
                        break;
                    }


                default:
                    throw new SWValidationException("NOT_SUPPORTED", "Search query not supported");
            }

            condition.Filters.Remove(idFilter);
        }

        var statusFilters = condition.Filters.Where(f => f.Field == "StatusFilter").ToList();
        foreach (var statusFilter in statusFilters)
        {
            query = query.WhereStatus(statusFilter.Value, dbContext);
            condition.Filters.Remove(statusFilter);
        }

        // "Only the newest attempt of each chain." An exchange that has been retried is superseded
        // by that retry, so a list of failures otherwise counts one piece of work as many rows -
        // a chain retried nine times fills nine of them, none of which is the current state.
        //
        // NOT EXISTS rather than a left join for the same reason the still-running status uses it:
        // Postgres estimates an anti-join, and RetryFor is indexed.
        var latestFilters = condition.Filters.Where(f => f.Field == "LatestOnly").ToList();
        foreach (var latestFilter in latestFilters)
        {
            if (IsOn(latestFilter))
                query = query.Where(i => !dbContext.Set<Xchange>().Any(r => r.RetryFor == i.Id));

            condition.Filters.Remove(latestFilter);
        }

        // "Only retries": attempts made from an earlier exchange, whether a person asked for them or
        // a retry policy did. RetryFor is on the exchange itself, so no second table is involved.
        var retryFilters = condition.Filters.Where(f => f.Field == "IsRetry").ToList();
        foreach (var retryFilter in retryFilters)
        {
            if (IsOn(retryFilter))
                query = Negated(retryFilter)
                    ? query.Where(i => i.RetryFor == null)
                    : query.Where(i => i.RetryFor != null);

            condition.Filters.Remove(retryFilter);
        }

        // "Waiting for a scheduled retry": a DelayedRetry row exists for it. That row is keyed by the
        // exchange's own id and deleted when the retry runs, so its presence is the whole question.
        // EXISTS rather than reading the search's left join to DelayedRetry, for the same reason
        // the still-running status uses NOT EXISTS - and because the lean projection bulk actions
        // and the summary filter over does not join DelayedRetry at all.
        var scheduledFilters = condition.Filters.Where(f => f.Field == "HasScheduledRetry").ToList();
        foreach (var scheduledFilter in scheduledFilters)
        {
            if (IsOn(scheduledFilter))
                query = Negated(scheduledFilter)
                    ? query.Where(i => !dbContext.Set<DelayedRetry>().Any(d => d.Id == i.Id))
                    : query.Where(i => dbContext.Set<DelayedRetry>().Any(d => d.Id == i.Id));

            condition.Filters.Remove(scheduledFilter);
        }

        // "Rolled up into an aggregation": the exchange was taken into an aggregation exchange,
        // which is what carries its data onwards from there.
        var aggregatedFilters = condition.Filters.Where(f => f.Field == "Aggregated").ToList();
        foreach (var aggregatedFilter in aggregatedFilters)
        {
            if (IsOn(aggregatedFilter))
                query = Negated(aggregatedFilter)
                    ? query.Where(i => i.AggregationXchangeId == null)
                    : query.Where(i => i.AggregationXchangeId != null);

            condition.Filters.Remove(aggregatedFilter);
        }

        // Text inside the error, case-insensitive. Named apart from the Exception column rather than
        // reusing it, because a plain Searchy filter on Exception would compare case as stored, and
        // an error message's case is whatever the adapter that threw it chose. Folded on both sides
        // like the promoted properties below; a leading-wildcard LIKE could use no index either way.
        // An empty value is no filter at all rather than "has any error", which is what Contains("")
        // would quietly turn into.
        var errorFilters = condition.Filters.Where(f => f.Field == "ErrorContains").ToList();
        foreach (var errorFilter in errorFilters)
        {
            var value = errorFilter.Value?.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                value = value.ToLower();
                // An exchange with no error doesn't contain the text either, so "does not contain"
                // keeps it - as "is not" on a subscription keeps the exchanges that have none.
                query = Negated(errorFilter)
                    ? query.Where(i => i.Exception == null || !i.Exception.ToLower().Contains(value))
                    : query.Where(i => i.Exception.ToLower().Contains(value));
            }

            condition.Filters.Remove(errorFilter);
        }

        // The one promoted property the value below has to be found in. Sent apart from the value
        // because the client used to send them together as "key:value", which only matched a value
        // that starts with what was typed: "SO-1001" was found by "SO", never by "1001".
        var keyFilters = condition.Filters.Where(f => f.Field == "PromotedPropertyKey").ToList();
        var key = keyFilters.Select(f => f.Value?.ToString()).FirstOrDefault(v => !string.IsNullOrEmpty(v))?.ToLower();
        var propertiesFilters = condition.Filters
            .Where(f => f.Field == "PromotedPropertiesRaw").ToList();

        // The key and the values below are one question - "orderId contains 1001" - so "not" turns
        // the whole of it round, never one part: an exchange whose orderId doesn't contain 1001, or
        // that has no orderId at all. Asked for one way in one part and the other way in another,
        // it has no reading worth guessing at, and bulk retry acts on whatever this selects.
        var negations = keyFilters.Concat(propertiesFilters).Select(Negated).Distinct().ToList();
        if (negations.Count > 1)
            throw new SWValidationException("NOT_SUPPORTED",
                "A promoted property filter is negated as a whole: send its key and its value with the same rule.");
        var negate = negations.SingleOrDefault();

        foreach (var keyFilter in keyFilters)
            condition.Filters.Remove(keyFilter);
        foreach (var propertyFilter in propertiesFilters)
            condition.Filters.Remove(propertyFilter);

        // An empty value is no filter, as with the error text above.
        var match = PropertyMatch(key, propertiesFilters
            .Select(f => f.Value?.ToString())
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v.ToLower()));
        if (match != null)
            query = query.Where(negate ? NoneOrNot(match) : match);

        // Any number of properties at once - "orderNo contains 1001" and "city is Paris" - each with
        // its key and text in the one filter, as they are stored: PromotedProperty:4:orderNo:1001.
        // A key alone asks whether the exchange promoted it. Every one has to hold, and rule 2 turns
        // each round on its own. The pair above stays for links and callers made before this; it can
        // only ask about one key, since it has no way to say which value goes with which.
        var pairFilters = condition.Filters.Where(f => f.Field == "PromotedProperty").ToList();
        foreach (var pairFilter in pairFilters)
        {
            var text = pairFilter.Value?.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                if (pairFilter.Rule is not (SearchyRule.EqualsTo or SearchyRule.Contains or SearchyRule.NotEqualsTo))
                    throw new SWValidationException("NOT_SUPPORTED",
                        "A promoted property is asked for with rule 1 or 4, or rule 2 to turn it round.");

                var colon = text.IndexOf(':');
                var pairKey = (colon < 0 ? text : text[..colon]).ToLower();
                var pairValue = colon < 0 ? "" : text[(colon + 1)..].ToLower();
                if (pairKey.Length == 0)
                    throw new SWValidationException("NOT_SUPPORTED",
                        "Name the promoted property: PromotedProperty:4:key:text. Text in any property is PromotedPropertiesRaw.");

                var pair = PropertyMatch(pairKey, pairValue.Length == 0 ? [] : [pairValue]);
                query = query.Where(Negated(pairFilter) ? NoneOrNot(pair) : pair);
            }

            condition.Filters.Remove(pairFilter);
        }

        return query;
    }

    /// <summary>
    /// An exchange whose promoted properties include <paramref name="key"/> (when there is one)
    /// and contain each of <paramref name="values"/> - inside that key's value when there is a key,
    /// anywhere when there is not. Both already lower-cased. Null when there is nothing to ask.
    /// </summary>
    private static Expression<Func<XchangeRow, bool>> PropertyMatch(string key, IEnumerable<string> values)
    {
        // Stored as "key:value,key:value": the key comes first or after a comma, which keeps "id"
        // from matching "orderId:". Asked of lower(properties_raw) as it is, not with a comma put in
        // front: Postgres keeps statistics for that expression (its trigram index), and without them
        // it guessed almost nothing matched, so a common key fetched and sorted every match rather
        // than reading the newest 25 - 6s against 1M exchanges.
        var keyTag = "," + key + ":";
        Expression<Func<XchangeRow, bool>> match = null;
        if (key != null)
            match = i => i.PromotedPropertiesRaw.ToLower().StartsWith(key + ":")
                         || i.PromotedPropertiesRaw.ToLower().Contains(keyTag);

        foreach (var value in values)
        {
            // Both sides lower-cased at query time. Promoted values keep the case the
            // payload had (see FilterService), so the column has to be folded here for
            // the search to stay case-insensitive. No index is lost: a Contains is a
            // leading-wildcard LIKE, which the b-tree on this column could never serve.
            match = match.And(i => i.PromotedPropertiesRaw.ToLower().Contains(value));

            // With a key, only inside that key's value: what follows ",key:" up to the next comma.
            // A value that itself holds a comma is only searched up to it, since the stored text
            // cannot tell that comma from the one before the next key. IndexOf rather than Contains
            // for the same estimate reason as the key above: Postgres guesses a third of rows pass
            // an unknown comparison but under 1% pass an unknown LIKE. "rest" - what follows the
            // key, with a comma added - is written out twice, as a query's `let` would inline it.
            if (key != null)
                match = match.And(i =>
                    (("," + i.PromotedPropertiesRaw.ToLower())
                        .Substring(("," + i.PromotedPropertiesRaw.ToLower()).IndexOf(keyTag) + keyTag.Length) + ",")
                    .Substring(0, (("," + i.PromotedPropertiesRaw.ToLower())
                        .Substring(("," + i.PromotedPropertiesRaw.ToLower()).IndexOf(keyTag) + keyTag.Length) + ",")
                        .IndexOf(","))
                    .IndexOf(value) >= 0);
        }

        return match;
    }

    /// <summary>A special filter asked for the other way round: rule 2, "not".</summary>
    private static bool Negated(SearchyFilter filter) => filter.Rule == SearchyRule.NotEqualsTo;

    /// <summary>Both, as one predicate - which the property filter needs, to be able to turn it round whole.</summary>
    private static Expression<Func<XchangeRow, bool>> And(this Expression<Func<XchangeRow, bool>> left,
        Expression<Func<XchangeRow, bool>> right) =>
        left == null
            ? right
            : Expression.Lambda<Func<XchangeRow, bool>>(
                Expression.AndAlso(left.Body,
                    ReplacingExpressionVisitor.Replace(right.Parameters[0], left.Parameters[0], right.Body)),
                left.Parameters);

    /// <summary>
    /// The property filter turned round, keeping the exchanges that promoted nothing. Spelt out
    /// rather than left to the negation: SQL's NOT of a LIKE over a null is null, which drops the row.
    /// </summary>
    private static Expression<Func<XchangeRow, bool>> NoneOrNot(Expression<Func<XchangeRow, bool>> match)
    {
        var row = match.Parameters[0];
        var raw = Expression.Property(row, nameof(XchangeRow.PromotedPropertiesRaw));
        return Expression.Lambda<Func<XchangeRow, bool>>(
            Expression.OrElse(Expression.Equal(raw, Expression.Constant(null, typeof(string))), Expression.Not(match.Body)),
            row);
    }

    /// <summary>
    /// Whether an on/off filter is on. Only "true" or "1" turn one on; anything else leaves it off
    /// rather than refusing it, which is what <c>LatestOnly</c> has always done - "select all
    /// matching" with no filters set sends <c>LatestOnly:1:false</c> to mean "everything".
    /// </summary>
    private static bool IsOn(SearchyFilter filter) =>
        filter.Value?.ToString()?.ToLower() is "true" or "1";

    /// <summary>
    /// Narrows <paramref name="query"/> to one exchange status, by the number the client's
    /// <c>StatusFilter</c> sends: 0 still running, 1 success, 2 success with a bad response, 3 failed.
    /// </summary>
    /// <remarks>
    /// Its own method so the summary can count each status under the same definition the list
    /// filters by. A count that defined "success" even slightly differently from the tab it sits on
    /// would show one number and list another.
    /// </remarks>
    internal static IQueryable<XchangeRow> WhereStatus(this IQueryable<XchangeRow> query, object status,
        BitweenDbContext dbContext)
    {
        switch (status)
        {
            case "0":
                // "Still running" means no result row exists yet. Asking for it as
                // Status == null reads as `x0.success IS NULL` on the left join, and
                // Postgres cannot estimate that: it guesses one row, plans every join
                // above it for one row, and picks per-row sequential scans of the small
                // side tables. Measured on 1M exchanges that was 22.8s for 25 rows.
                // NOT EXISTS asks the same question as an anti-join, which it can
                // estimate - 34ms. Equivalent because success is NOT NULL, so a result
                // row can never itself carry a null status.
                return query.Where(i =>
                    !dbContext.Set<XchangeResult>().Any(r => r.Id == i.Id));
            case "1":
                return query.Where(i => i.Status == true && i.ResponseBad != true);

            case "2":
                return query.Where(i => i.Status == true && i.ResponseBad == true);

            case "3":
                return query.Where(i => i.Status == false);

            default:
                // The filter is removed by the caller whether or not it matched, so falling through
                // here used to drop it silently and widen the selection to everything. A search
                // returning too much is merely wrong; bulk retry runs over whatever this selects,
                // so an unreadable status has to be refused rather than ignored.
                throw new SWValidationException("NOT_SUPPORTED",
                    $"'{status}' is not an exchange status.");
        }
    }
}
