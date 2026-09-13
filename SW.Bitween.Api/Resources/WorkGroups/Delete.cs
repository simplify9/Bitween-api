using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.WorkGroups;

[HandlerName(nameof(Delete))]
public class Delete(BitweenDbContext dbContext, RequestContext requestContext, IBroadcast _broadcast, IInfolinkCache _infolinkCache)
    : ICommandHandler<int, DeleteWorkGroupModel, object>
{
    public async Task<object> Handle(int key, DeleteWorkGroupModel _)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.WorkGroups.Delete);

        var category = await dbContext.Set<WorkGroup>().FindAsync(key);
        if (category is null)
            throw new SWValidationException("CATEGORY_NOT_FOUND", $"Workgroup with id {key} was not found");

        if (await dbContext.Set<Subscription>().AnyAsync(i => i.WorkGroupId.Value == category.Id))
            throw new SWValidationException("CANT_BE_DELETED", "Workgroup with Subscriptions cant be deleted");

        // Data source statements run on a work group too, and that foreign key restricts as well —
        // left unchecked it refused the delete as a bare 500 naming the constraint.
        var statements = await dbContext.Set<DataSourceStatement>()
            .Where(s => s.WorkGroupId == key)
            .Select(s => s.Name)
            .Take(5)
            .ToListAsync();
        if (statements.Count > 0)
            throw new SWValidationException("CANT_BE_DELETED",
                "Cannot delete a work group that data source statements run on: " +
                string.Join(", ", statements) +
                ". Point them at another work group first.");

        //Todo chek rabbitMq
        dbContext.Remove(category);
        await dbContext.SaveChangesAsync();
        await _infolinkCache.BroadcastRevoke();
        await _broadcast.RefreshConsumers();
        return null;
    }
}