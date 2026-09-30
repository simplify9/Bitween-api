using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

public class Update(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, RetryPolicyUpdate, object>
{
    public async Task<object> Handle(int key, RetryPolicyUpdate model)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.RetryPolicies.Edit);
        RetryGroupValidation.EnsureCanFire(model.Groups);
        await RetryGroupValidation.EnsureAlertChannelsExist(dbContext,
            [model.AlertChannelId, ..RetryGroupValidation.AlertChannelIds(model.Groups)]);

        var entity = await dbContext.FindAsync<RetryPolicy>(key);

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
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        entity.Name = model.Name;
        entity.Groups = model.Groups ?? [];
        entity.AlertChannelId = model.AlertChannelId;
        await dbContext.SaveChangesAsync();

        if (removedGroupIds.Count > 0)
        {
            await dbContext.Set<RetryGroupUsage>()
                .Where(u => removedGroupIds.Contains(u.GroupId))
                .ExecuteDeleteAsync();

            // Alert overrides are keyed by group id for the same reason usage is, so they strand the
            // same way when a group disappears.
            await dbContext.Set<RetryAlertOverride>()
                .Where(o => removedGroupIds.Contains(o.GroupId))
                .ExecuteDeleteAsync();
        }

        await transaction.CommitAsync();
        return null;
    }
}
