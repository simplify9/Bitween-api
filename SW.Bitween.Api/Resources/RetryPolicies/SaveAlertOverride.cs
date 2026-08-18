using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

/// <summary>
/// Sets, changes or clears where one subscription's alerts go for one group of this policy — the most
/// specific level of the hierarchy.
/// </summary>
[HandlerName("savealertoverride")]
public class SaveAlertOverride : ICommandHandler<int, RetryAlertOverrideSave, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public SaveAlertOverride(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(int key, RetryAlertOverrideSave request)
    {
        _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);
        RetryGroupValidation.EnsureAlertCanSend(request.AlertMode, request.AlertHandlerId);

        var policy = await _dbContext.Set<RetryPolicy>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == key);
        if (policy == null) throw new SWNotFoundException(key.ToString());

        // Scoped to this policy's own groups and subscriptions, so a policy id in the route cannot
        // reach an override belonging to a different policy.
        if (policy.Groups.All(g => g.Id != request.GroupId))
            throw new SWValidationException("GROUP_NOT_IN_POLICY",
                "That group does not belong to this retry policy.");

        var usesPolicy = await _dbContext.Set<Subscription>()
            .AnyAsync(s => s.Id == request.SubscriptionId && s.RetryPolicyId == key);
        if (!usesPolicy)
            throw new SWValidationException("SUBSCRIPTION_NOT_USING_POLICY",
                "That subscription does not use this retry policy.");

        var existing = await _dbContext.Set<RetryAlertOverride>()
            .FirstOrDefaultAsync(o => o.SubscriptionId == request.SubscriptionId
                                      && o.GroupId == request.GroupId);

        // Inherit is the absence of an override, so store nothing rather than a row that does nothing
        // — otherwise the routing list would have to explain a row that changes no behaviour.
        if (request.AlertMode == RetryAlertMode.Inherit)
        {
            if (existing != null) _dbContext.Remove(existing);
            await _dbContext.SaveChangesAsync();
            return null;
        }

        if (existing == null)
        {
            _dbContext.Add(new RetryAlertOverride
            {
                SubscriptionId = request.SubscriptionId,
                GroupId = request.GroupId,
                AlertMode = request.AlertMode,
                AlertHandlerId = request.AlertHandlerId,
                AlertHandlerProperties = request.AlertHandlerProperties
            });
        }
        else
        {
            existing.AlertMode = request.AlertMode;
            existing.AlertHandlerId = request.AlertHandlerId;
            existing.AlertHandlerProperties = request.AlertHandlerProperties;
        }

        await _dbContext.SaveChangesAsync();
        return null;
    }
}
