using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Model;
using SW.Bitween.Services.DataSources;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.DataSourceStatements;

/// <summary>
/// Every statement, with the count that matters: how many subscriptions name it.
///
/// Zero is the whole point. Without it, SQL nobody uses accumulates forever because nobody can
/// prove it is safe to remove - which is exactly what happened while these lived in one JSON blob.
/// </summary>
public class Search(BitweenDbContext dbContext, RequestContext requestContext,
    StatementUsageReader usage) : ISearchyHandler
{
    public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false,
        string searchPhrase = null)
    {
        if (!lookup)
            await requestContext.EnsurePermission(dbContext,
                Model.Permissions.DataSourceStatements.View);

        var query = from statement in dbContext.Set<DataSourceStatement>()
            select new DataSourceStatementRow
            {
                Id = statement.Id,
                DataSourceId = statement.DataSourceId,
                Name = statement.Name,
                Sql = statement.Sql,
                CursorColumn = statement.CursorColumn,
                KeyColumn = statement.KeyColumn,
                Description = statement.Description,
                WorkGroupId = statement.WorkGroupId,
                WorkGroupName = dbContext.Set<WorkGroup>()
                    .Where(w => w.Id == statement.WorkGroupId)
                    .Select(w => w.Name).FirstOrDefault(),
                Inactive = statement.Inactive,
                CreatedOn = statement.CreatedOn,
                CreatedBy = statement.CreatedBy,
                ModifiedOn = statement.ModifiedOn,
                ModifiedBy = statement.ModifiedBy
            };

        query = query.AsNoTracking();

        if (lookup)
            return await query.Search(searchyRequest.Conditions)
                .ToDictionaryAsync(k => k.Id.ToString(), v => v.Name);

        var rows = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts,
            searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync();

        await FillUsageAsync(rows);

        return new SearchyResponse<DataSourceStatementRow>
        {
            TotalCount = await query.Search(searchyRequest.Conditions).CountAsync(),
            Result = rows
        };
    }

    /// <summary>
    /// One usage read per data source on the page, not one per statement: usage is resolved by
    /// scanning that data source's subscriptions, and doing it per row would repeat the same scan
    /// for every statement the connection has.
    /// </summary>
    async Task FillUsageAsync(List<DataSourceStatementRow> rows)
    {
        foreach (var group in rows.GroupBy(r => r.DataSourceId))
        {
            var forSource = await usage.ForDataSourceAsync(group.Key);

            foreach (var row in group)
                row.UsageCount = forSource.TryGetValue(row.Name, out var entries) ? entries.Count : 0;
        }
    }
}
