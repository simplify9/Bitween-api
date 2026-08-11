using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
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
        _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);
        RetryGroupValidation.EnsureCanFire(model.Groups);

        var entity = await _dbContext.FindAsync<RetryPolicy>(key);

        // Spent budget is keyed by group id, so a group removed here would leave usage rows
        // that no policy claims — invisible to the usage report and beyond the reach of reset.
        var removedGroupIds = entity.Groups
            .Select(g => g.Id)
            .Except((model.Groups ?? []).Select(g => g.Id))
            .ToList();

        entity.Name = model.Name;
        entity.Groups = model.Groups ?? [];
        await _dbContext.SaveChangesAsync();

        if (removedGroupIds.Count > 0)
            await _dbContext.Set<RetryGroupUsage>()
                .Where(u => removedGroupIds.Contains(u.GroupId))
                .ExecuteDeleteAsync();

        return null;
    }
}
