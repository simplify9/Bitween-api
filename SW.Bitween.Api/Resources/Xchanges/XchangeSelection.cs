using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Xchanges;

/// <summary>
/// The exchanges a bulk action on the exchange list covers - a bulk retry, a files export: the rows
/// picked by hand, or everything a "select all matching" filter matches minus the rows unticked after.
/// </summary>
/// <remarks>
/// Built by hand rather than injected, like <see cref="BulkRetryPlanner"/>: a piece of one request's
/// work, not a service.
/// </remarks>
internal sealed class XchangeSelection(BitweenDbContext dbContext)
{
    /// <summary>
    /// The selected ids. For a filter, at most <paramref name="limit"/> + 1 of them: one past the limit
    /// is all it takes to know the selection is too big, and stops a "select all" over a wide filter
    /// from reading a million ids to refuse them.
    /// </summary>
    internal async Task<List<string>> Resolve(IXchangeSelection request, int limit)
    {
        if (string.IsNullOrWhiteSpace(request.Filter))
            return (request.Ids ?? new List<string>()).Where(id => id != null).Distinct().ToList();

        var exclude = request.ExcludeIds ?? new List<string>();

        return await (await Query(request))
            .Where(r => !exclude.Contains(r.Id))
            .Select(r => r.Id)
            .Take(limit + 1)
            .ToListAsync();
    }

    /// <summary>How many exchanges a filter's selection really holds, for saying how far past a limit it is.</summary>
    internal async Task<int> Count(IXchangeSelection request)
    {
        var exclude = request.ExcludeIds ?? new List<string>();
        return await (await Query(request))
            .Where(r => !exclude.Contains(r.Id))
            // Same reason the search caps its own count: counting every match has to visit every
            // matching row. The number is only being used to say "too many", so stopping early
            // costs the caller nothing.
            .Take(Search.CountCap + 1)
            .CountAsync();
    }

    /// <summary>
    /// The exchanges a "select all matching" came from, filtered exactly as the search would have
    /// filtered them.
    /// </summary>
    /// <remarks>
    /// The query itself lives in <see cref="XchangeFilters.FilteredRowsAsync"/>, shared with the
    /// summary so the counts above the list and the set a bulk action acts on are the same rows -
    /// read its remarks before filtering on a new column.
    /// </remarks>
    private Task<IQueryable<XchangeRow>> Query(IXchangeSelection request) =>
        XchangeFilters.FilteredRowsAsync(dbContext, new SearchyRequest(request.Filter));
}
