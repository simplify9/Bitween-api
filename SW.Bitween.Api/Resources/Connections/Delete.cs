using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Connections;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Connections;

public class Delete(BitweenDbContext dbContext, RequestContext requestContext) : ICommandHandler<int, ConnectionDelete, object>
{
    public async Task<object> Handle(int key, ConnectionDelete request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.GlobalValues.Delete);
        var connection = await dbContext.Set<Connection>().SingleOrDefaultAsync(c => c.Id == key)
                         ?? throw new SWNotFoundException(key.ToString());
        var uses = ConnectionRows.UsesOf(connection.Name, dbContext).ToList();
        if (uses.Count > 0)
            throw new SWValidationException("CONNECTION_IN_USE", $"'{connection.Name}' is still used by {string.Join(", ", uses)}.");
        dbContext.Remove(connection);
        await dbContext.SaveChangesAsync();
        return null;
    }
}
