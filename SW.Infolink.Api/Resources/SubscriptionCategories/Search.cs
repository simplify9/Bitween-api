using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.SubscriptionCategories;

public class Search : IQueryHandler<SearchSubscriptionCategoryModel>
{
    private readonly InfolinkDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Search(InfolinkDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(SearchSubscriptionCategoryModel request)
    {
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