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
        return await _dbContext.Set<RetryPolicy>()
            .AsNoTracking()
            .Search("Id", key)
            .Select(p => new RetryPolicyUpdate
            {
                Name = p.Name,
                Groups = p.Groups
            })
            .SingleOrDefaultAsync();
    }
}
