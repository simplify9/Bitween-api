using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.WorkGroups;

[HandlerName(nameof(Delete))]
public class Delete(BitweenDbContext dbContext, RequestContext requestContext, IBroadcast _broadcast, IInfolinkCache _infolinkCache)
    : ICommandHandler<int, DeleteWorkGroupModel, object>
{
    private readonly RequestContext _requestContext = requestContext;

    public async Task<object> Handle(int key, DeleteWorkGroupModel _)
    {
        await _requestContext.EnsurePermission(dbContext, Model.Permissions.WorkGroups.Delete);

        var category = await dbContext.Set<WorkGroup>().FindAsync(key);
        if (category is null)
            throw new SWValidationException("CATEGORY_NOT_FOUND", $"Workgroup with id {key} was not found");

        if (await dbContext.Set<Subscription>().AnyAsync(i => i.WorkGroupId.Value == category.Id))
            throw new SWValidationException("CANT_BE_DELETED", "Workgroup with Subscriptions cant be deleted");

        //Todo chek rabbitMq
        dbContext.Remove(category);
        await dbContext.SaveChangesAsync();
        await _infolinkCache.BroadcastRevoke();
        await _broadcast.RefreshConsumers();
        return null;
    }
}