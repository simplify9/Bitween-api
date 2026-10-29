using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Services.DataSources;
using SW.PrimitiveTypes;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.DataSourceStatements;

/// <summary>
/// Refused while anything names it, and the refusal lists what. A statement deleted out from under
/// a live subscription does not fail at delete time - it fails on the next message, as "not a
/// statement this data source defines", somewhere nobody is watching.
/// </summary>
public class Delete(BitweenDbContext dbContext, RequestContext requestContext,
    StatementUsageReader usage) : IDeleteHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.DataSourceStatements.Delete);

        var statement = await dbContext.Set<DataSourceStatement>().FirstOrDefaultAsync(s => s.Id == key);
        if (statement == null)
            throw new SWNotFoundException($"DataSourceStatement with id '{key}' was not found");

        var forSource = await usage.ForDataSourceAsync(statement.DataSourceId);
        if (forSource.TryGetValue(statement.Name, out var users) && users.Count > 0)
            throw new SWException(
                $"'{statement.Name}' is named by {users.Count} subscription(s): "
                + string.Join(", ", users.Select(u => $"{u.SubscriptionName} ({u.Role})").Distinct())
                + ". Point them elsewhere first, or mark the statement inactive to retire it while "
                + "they are migrated.");

        dbContext.Remove(statement);
        await dbContext.SaveChangesAsync();
        return statement.Id;
    }
}
