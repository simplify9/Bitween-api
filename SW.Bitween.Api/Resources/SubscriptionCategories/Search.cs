using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.SubscriptionCategories;

public class Search : IQueryHandler<SearchSubscriptionCategoryModel,object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Search(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(SearchSubscriptionCategoryModel request)
    {
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Subscriptions.View);

        request.Limit ??= 20;
        request.Offset ??= 0;
        var q = _dbContext.Set<SubscriptionCategory>().AsNoTracking().AsQueryable();

        var count = await q.CountAsync();

        var data = await q
            .OrderByDescending(i => i.Id)
            .Skip(request.Offset.Value)
            .Take(request.Limit.Value)
            .Select(i => new SubscriptionCategoryModel
            {
                Id = i.Id,
                Code = i.Code,
                Description = i.Description,
                CreatedOn = i.CreatedOn
            }).ToListAsync();


        return new
        {
            Result = data,
            TotalCount = count
        };
    }
}