using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.SubscriptionCategories;

[HandlerName("delete")]
public class Delete : ICommandHandler<int, DeleteSubscriptionCategoryModel>
{
    private readonly InfolinkDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Delete(InfolinkDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(int key, DeleteSubscriptionCategoryModel _)
    {
        var category = await _dbContext.Set<SubscriptionCategory>().FindAsync(key);
        if (category is null)
            throw new SWValidationException("CATEGORY_NOT_FOUND", $"Category with id {key} was not found");

        if (await _dbContext.Set<Subscription>().AnyAsync(i => i.CategoryId.Value == category.Id))
            throw new SWValidationException("CANT_BE_DELETED", "Categories with Subscriptions cant be deleted");

        _dbContext.Remove(category);
        await _dbContext.SaveChangesAsync();
        return null;
    }
}