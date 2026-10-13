using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Connections;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Connections;

public class Get(BitweenDbContext dbContext, RequestContext requestContext) : IGetHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.GlobalValues.View);
        var connection = await dbContext.Set<Connection>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == key)
                         ?? throw new SWNotFoundException(key.ToString());
        return ConnectionRows.Row(connection);
    }
}
