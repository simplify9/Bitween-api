using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

public class Update : ICommandHandler<int, RetryPolicyUpdate, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Update(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(int key, RetryPolicyUpdate model)
    {
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.RetryPolicies.Edit);
        RetryGroupValidation.EnsureCanFire(model.Groups);
        RetryGroupValidation.EnsureAlertTransportIsSecure(
            model.AlertHandlerId, model.AlertHandlerProperties);

        var entity = await _dbContext.FindAsync<RetryPolicy>(key);

        // Spent budget is keyed by group id, so a group removed here would leave usage rows
        // that no policy claims — invisible to the usage report and beyond the reach of reset.
        var removedGroupIds = entity.Groups
            .Select(g => g.Id)
            .Except((model.Groups ?? []).Select(g => g.Id))
            .ToList();

        // Dropping the groups and clearing what belonged to them is one change, so it commits as
        // one: a group no policy claims whose usage and override rows survive is unreachable from
        // the usage report and from reset alike. Safe to span, because RetryPolicy raises no domain
        // events — nothing reaches the bus before the commit.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();

        // Secrets came out of Get masked, so put them back from what this same level already holds.
        // A group matched by id, because a group added in this very save has nothing to restore from.
        foreach (var group in model.Groups ?? [])
        {
            var storedGroup = entity.Groups.FirstOrDefault(g => g.Id == group.Id);
            AdapterSecretProperties.MergeInPlace(
                storedGroup?.AlertHandlerProperties, group.AlertHandlerProperties);
        }

        var storedPolicyProperties = entity.AlertHandlerProperties;

        entity.Name = model.Name;
        entity.Groups = model.Groups ?? [];
        entity.AlertHandlerId = model.AlertHandlerId;
        entity.AlertHandlerProperties =
            AdapterSecretProperties.Merge(storedPolicyProperties, model.AlertHandlerProperties);
        await _dbContext.SaveChangesAsync();

        if (removedGroupIds.Count > 0)
        {
            await _dbContext.Set<RetryGroupUsage>()
                .Where(u => removedGroupIds.Contains(u.GroupId))
                .ExecuteDeleteAsync();

            // Alert overrides are keyed by group id for the same reason usage is, so they strand the
            // same way when a group disappears.
            await _dbContext.Set<RetryAlertOverride>()
                .Where(o => removedGroupIds.Contains(o.GroupId))
                .ExecuteDeleteAsync();
        }

        await transaction.CommitAsync();
        return null;
    }
}
