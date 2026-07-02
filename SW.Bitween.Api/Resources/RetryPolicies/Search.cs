using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

public class Search : ISearchyHandler
{
    private readonly BitweenDbContext _dbContext;

    public Search(BitweenDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
    {
        var query = from policy in _dbContext.Set<RetryPolicy>()
            select new RetryPolicyRow
            {
                Id = policy.Id,
                Name = policy.Name,
                GroupCount = policy.Groups.Count
            };

        query = query.AsNoTracking();

        if (lookup)
            return await query.Search(searchyRequest.Conditions)
                .ToDictionaryAsync(k => k.Id.ToString(), v => v.Name);

        return new SearchyResponse<RetryPolicyRow>
        {
            TotalCount = await query.Search(searchyRequest.Conditions).CountAsync(),
            Result = await query.Search(searchyRequest.Conditions, searchyRequest.Sorts,
                searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync()
        };
    }
}
