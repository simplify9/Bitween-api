using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Connections;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Connections;

public class Update(BitweenDbContext dbContext, RequestContext requestContext) : ICommandHandler<int, ConnectionSave, object>
{
    public async Task<object> Handle(int key, ConnectionSave request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.GlobalValues.Edit);
        var connection = await dbContext.Set<Connection>().SingleOrDefaultAsync(c => c.Id == key)
                         ?? throw new SWNotFoundException(key.ToString());
        var before = connection.Name;
        ConnectionRows.Apply(connection, request);

        if (!string.Equals(before, connection.Name, System.StringComparison.OrdinalIgnoreCase))
        {
            var lowered = connection.Name.ToLowerInvariant();
            if (await dbContext.Set<Connection>().AnyAsync(c => c.Id != key && c.Name.ToLower() == lowered))
                throw new SWValidationException("CONNECTION_NAME_TAKEN", $"There is already a connection named '{connection.Name}'.");
            // Settings refer to it by name, so a rename would quietly break every one of them.
            var uses = string.Join(", ", ConnectionRows.UsesOf(before, dbContext));
            if (uses.Length > 0)
                throw new SWValidationException("CONNECTION_IN_USE", $"'{before}' can't be renamed while settings refer to it: {uses}.");
        }

        await dbContext.SaveChangesAsync();
        return ConnectionRows.Row(connection);
    }
}
