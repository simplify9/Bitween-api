using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Audit;

/// <summary>
/// The audit trail, newest first. Serves both the global history page and the per-entity timeline -
/// the latter is this same query with <see cref="SearchAuditModel.EntityName"/> and
/// <see cref="SearchAuditModel.EntityKey"/> set, which is what the composite index covers.
/// </summary>
public class Search(BitweenDbContext dbContext, RequestContext requestContext)
    : IQueryHandler<SearchAuditModel, object>
{
    /// <summary>Rows per page ceiling - each row carries its changes as JSON.</summary>
    const int MaxLimit = 200;

    public async Task<object> Handle(SearchAuditModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Audit.View);

        // Both arrive from the query string. A negative Skip is a database error rather than an
        // empty page, and this table's rows carry a JSON blob each, so an unbounded Take is the
        // one page size worth refusing.
        request.Limit = Math.Clamp(request.Limit ?? 20, 1, MaxLimit);
        request.Offset = Math.Max(request.Offset ?? 0, 0);

        var query = dbContext.Set<AuditEntry>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.EntityName))
            query = query.Where(e => e.EntityName == request.EntityName);

        if (!string.IsNullOrWhiteSpace(request.EntityKey))
            query = query.Where(e => e.EntityKey == request.EntityKey);

        if (!string.IsNullOrWhiteSpace(request.UserId))
            query = query.Where(e => e.UserId == request.UserId);

        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
            query = query.Where(e => e.CorrelationId == request.CorrelationId);

        if (request.From.HasValue)
            query = query.Where(e => e.OccurredOn >= request.From.Value);

        if (request.To.HasValue)
            query = query.Where(e => e.OccurredOn <= request.To.Value);

        var totalCount = await query.CountAsync();

        var rows = await query
            // Sequence breaks the tie within one save, where every row shares a timestamp; Id
            // breaks it between two saves that landed in the same tick, without which paging
            // could show one row twice and skip another.
            .OrderByDescending(e => e.OccurredOn)
            .ThenByDescending(e => e.Sequence)
            .ThenByDescending(e => e.Id)
            .Skip(request.Offset.Value).Take(request.Limit.Value)
            .ToListAsync();

        return new
        {
            Result = await ToModels(dbContext, rows),
            TotalCount = totalCount
        };
    }

    /// <summary>
    /// Turns stored rows into models, resolving actor names in one query for the whole page rather
    /// than one per row.
    /// </summary>
    static async Task<List<AuditEntryModel>> ToModels(
        BitweenDbContext dbContext, IReadOnlyCollection<AuditEntry> rows)
    {
        var accountIds = rows
            .Select(e => int.TryParse(e.UserId, out var id) ? id : (int?)null)
            .Where(id => id.HasValue).Select(id => id.Value).Distinct().ToList();

        var names = accountIds.Count == 0
            ? new Dictionary<int, string>()
            : await dbContext.Set<Account>().AsNoTracking()
                .Where(a => accountIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id, a => a.DisplayName);

        return rows.Select(e => new AuditEntryModel
        {
            Id = e.Id,
            CorrelationId = e.CorrelationId,
            Sequence = e.Sequence,
            OccurredOn = e.OccurredOn,
            UserId = e.UserId,
            UserDisplayName = int.TryParse(e.UserId, out var accountId)
                ? names.GetValueOrDefault(accountId)
                : null,
            EntityName = e.EntityName,
            EntityKey = e.EntityKey,
            State = e.State,
            Changes = JsonConvert.DeserializeObject<Dictionary<string, AuditChangeModel>>(e.Changes)
        }).ToList();
    }
}
