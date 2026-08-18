using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

public class Get : IGetHandler<int, object>
{
    private readonly BitweenDbContext _dbContext;

    public Get(BitweenDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<object> Handle(int key)
    {
        // Materialize first: AlertHandlerProperties is a JSON-converted dictionary, and EF cannot
        // translate a further .ToDictionary() over it into SQL inside a projection.
        var policy = await _dbContext.Set<RetryPolicy>()
            .AsNoTracking()
            .Search("Id", key)
            .SingleOrDefaultAsync();

        if (policy == null) return null;

        return new RetryPolicyUpdate
        {
            Name = policy.Name,
            Groups = policy.Groups,
            AlertHandlerId = policy.AlertHandlerId,
            AlertHandlerProperties = policy.AlertHandlerProperties?.ToDictionary(kv => kv.Key, kv => kv.Value)
        };
    }
}
