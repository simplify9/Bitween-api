using System.Threading.Tasks;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.SubscriptionCategories;

public class Update : ICommandHandler<int, CreateSubscriptionCategoryModel>
{
    private readonly InfolinkDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Update(InfolinkDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(int key, CreateSubscriptionCategoryModel request)
    {
        var category = await _dbContext.Set<SubscriptionCategory>().FindAsync(key);
        if (category is null)
            throw new SWValidationException("CATEGORY_NOT_FOUND", $"Category with id {key} was not found");
        category.Update(request.Code, request.Description);
        await _dbContext.SaveChangesAsync();
        return null;
    }
}