using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.SubscriptionCategories;

public class Create : ICommandHandler<CreateSubscriptionCategoryModel>
{
    private readonly InfolinkDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Create(InfolinkDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(CreateSubscriptionCategoryModel request)
    {
        var category = new SubscriptionCategory(request.Code, request.Description);
        _dbContext.Add(category);
        await _dbContext.SaveChangesAsync();
        return new
        {
            category.Id
        };
    }
}