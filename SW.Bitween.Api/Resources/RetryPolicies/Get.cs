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
    private readonly AdapterSecretProperties _secrets;

    public Get(BitweenDbContext dbContext, AdapterSecretProperties secrets)
    {
        _dbContext = dbContext;
        _secrets = secrets;
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

        // Every level that can carry a handler can carry that handler's password, so every level is
        // masked. Groups are edited in place by the caller, which is what Update then merges back.
        foreach (var group in policy.Groups)
            await _secrets.MaskInPlace(group.AlertHandlerId, group.AlertHandlerProperties);

        return new RetryPolicyUpdate
        {
            Name = policy.Name,
            Groups = policy.Groups,
            AlertHandlerId = policy.AlertHandlerId,
            AlertHandlerProperties =
                await _secrets.Mask(policy.AlertHandlerId, policy.AlertHandlerProperties)
        };
    }
}
