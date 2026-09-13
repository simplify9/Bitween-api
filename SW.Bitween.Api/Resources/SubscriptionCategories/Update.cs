using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.SubscriptionCategories;

public class Update(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, CreateSubscriptionCategoryModel,object>
{
    public async Task<object> Handle(int key, CreateSubscriptionCategoryModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.Edit);

        var category = await dbContext.Set<SubscriptionCategory>().FindAsync(key);
        if (category is null)
            throw new SWValidationException("CATEGORY_NOT_FOUND", $"Category with id {key} was not found");

        await Create.EnsureCodeIsFree(dbContext, request.Code, key);

        category.Update(request.Code, request.Description);
        await dbContext.SaveChangesAsync();
        return null;
    }
}