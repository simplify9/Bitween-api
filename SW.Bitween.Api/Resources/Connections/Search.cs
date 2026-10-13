using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Connections;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Connections;

/// <summary>Every connection, as the Connections page and the reference picker list them.</summary>
public class Search(BitweenDbContext dbContext, RequestContext requestContext) : ISearchyHandler
{
    public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.GlobalValues.View);
        var connections = await dbContext.Set<Connection>().AsNoTracking().OrderBy(c => c.Name).ToListAsync();
        if (lookup) return connections.ToDictionary(c => c.Id, c => c.Name);
        return new SearchyResponse<Model.ConnectionRow>
        {
            Result = connections.Select(ConnectionRows.Row).ToList(),
            TotalCount = connections.Count,
        };
    }
}
