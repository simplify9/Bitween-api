using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Model;
using SW.Bitween.Services.DataSources;
using SW.PrimitiveTypes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.DataSourceStatements;

/// <summary>
/// Not just how many, but WHICH - with the slot and the operation, so "can I change this?" is
/// answerable without opening every subscription bound to the connection.
/// </summary>
[HandlerName("usage")]
public class Usage(BitweenDbContext dbContext, RequestContext requestContext,
    StatementUsageReader usage) : ICommandHandler<int, DataSourceStatementUsageRequest, object>
{
    public async Task<object> Handle(int key, DataSourceStatementUsageRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.DataSourceStatements.View);

        var statement = await dbContext.Set<DataSourceStatement>().AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == key);

        if (statement == null)
            throw new SWNotFoundException($"DataSourceStatement with id '{key}' was not found");

        var forSource = await usage.ForDataSourceAsync(statement.DataSourceId);

        return new DataSourceStatementUsage
        {
            StatementId = statement.Id,
            Name = statement.Name,
            UsedBy = forSource.TryGetValue(statement.Name, out var entries)
                ? entries
                : new List<DataSourceStatementUsageEntry>()
        };
    }
}
