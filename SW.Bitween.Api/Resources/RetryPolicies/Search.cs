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
    private readonly RequestContext _requestContext;

    public Search(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
    {
        // Lookup returns only id/name pairs, which pickers across the app rely on;
        // the full list is the data, so that's what the view permission covers.
        if (!lookup)
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.RetryPolicies.View);

        var query = from policy in _dbContext.Set<RetryPolicy>()
            select new RetryPolicyRow
            {
                Id = policy.Id,
                Name = policy.Name,
                GroupCount = policy.Groups.Count,
                // A correlated count, so the "used by" column the UI shows costs one subquery per
                // row instead of the whole Subscription table over the wire.
                UsedByCount = _dbContext.Set<Subscription>()
                    .Count(subscription => subscription.RetryPolicyId == policy.Id)
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
