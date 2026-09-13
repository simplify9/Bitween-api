using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.SubscriptionCategories;

public class Create(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<CreateSubscriptionCategoryModel,object>
{
    public async Task<object> Handle(CreateSubscriptionCategoryModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.Create);

        await EnsureCodeIsFree(dbContext, request.Code);

        var category = new SubscriptionCategory(request.Code, request.Description);
        dbContext.Add(category);
        await dbContext.SaveChangesAsync();
        return new
        {
            category.Id
        };
    }

    /// <summary>
    /// The code is uniquely indexed, so a repeat was already refused — as a constraint violation
    /// surfacing as a bare 500, which says nothing about the code being taken.
    /// </summary>
    internal static async Task EnsureCodeIsFree(BitweenDbContext dbContext, string code, int? existingId = null)
    {
        var taken = await dbContext.Set<SubscriptionCategory>().AsNoTracking()
            .AnyAsync(c => c.Code == code && c.Id != existingId);

        if (taken)
            throw new SWValidationException("CATEGORY_CODE_TAKEN",
                $"A category with the code '{code}' already exists.");
    }
}