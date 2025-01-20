using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.SubscriptionCategories;

public class Create : ICommandHandler<CreateSubscriptionCategoryModel,object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Create(BitweenDbContext dbContext, RequestContext requestContext)
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