using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.WorkGroups;

[HandlerName(nameof(Delete))]
public class Delete : ICommandHandler<int, DeleteWorkGroupModel,object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Delete(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(int key, DeleteWorkGroupModel _)
    {
        var category = await _dbContext.Set<WorkGroup>().FindAsync(key);
        if (category is null)
            throw new SWValidationException("CATEGORY_NOT_FOUND", $"Workgroup with id {key} was not found");

        if (await _dbContext.Set<Subscription>().AnyAsync(i => i.WorkGroupId.Value == category.Id))
            throw new SWValidationException("CANT_BE_DELETED", "Workgroup with Subscriptions cant be deleted");

        //Todo chek rabbitMq
        _dbContext.Remove(category);
        await _dbContext.SaveChangesAsync();
        return null;
    }
}