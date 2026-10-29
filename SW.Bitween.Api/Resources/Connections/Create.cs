using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Connections;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Connections;

public class Create(BitweenDbContext dbContext, RequestContext requestContext) : ICommandHandler<ConnectionSave, object>
{
    public async Task<object> Handle(ConnectionSave request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.GlobalValues.Create);
        var connection = new Connection();
        ConnectionRows.Apply(connection, request);
        var lowered = connection.Name.ToLowerInvariant();
        if (await dbContext.Set<Connection>().AnyAsync(c => c.Name.ToLower() == lowered))
            throw new SWValidationException("CONNECTION_NAME_TAKEN", $"There is already a connection named '{connection.Name}'.");
        dbContext.Add(connection);
        await dbContext.SaveChangesAsync();
        return ConnectionRows.Row(connection);
    }
}
