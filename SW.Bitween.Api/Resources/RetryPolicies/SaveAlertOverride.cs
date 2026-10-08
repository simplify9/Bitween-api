using System.Collections.Generic;
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
public class SaveAlertOverride(BitweenDbContext dbContext, RequestContext requestContext) : ICommandHandler<int, RetryAlertOverrideSave, object>
{
    public async Task<object> Handle(int key, RetryAlertOverrideSave request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.RetryPolicies.Edit);
        RetryGroupValidation.EnsureAlertCanSend(request.AlertMode, request.AlertHandlerId);
        RetryGroupValidation.EnsureAlertTransportIsSecure(
            request.AlertHandlerId, request.AlertHandlerProperties);

        var policy = await dbContext.Set<RetryPolicy>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == key);
        if (policy == null) throw new SWNotFoundException(key.ToString());

        // Scoped to this policy's own groups and subscriptions, so a policy id in the route cannot
        // reach an override belonging to a different policy.
        if (policy.Groups.All(g => g.Id != request.GroupId))
            throw new SWValidationException("GROUP_NOT_IN_POLICY",
                "That group does not belong to this retry policy.");

        var usesPolicy = await dbContext.Set<Subscription>()
            .AnyAsync(s => s.Id == request.SubscriptionId && s.RetryPolicyId == key);
        if (!usesPolicy)
            throw new SWValidationException("SUBSCRIPTION_NOT_USING_POLICY",
                "That subscription does not use this retry policy.");

        var existing = await dbContext.Set<RetryAlertOverride>()
            .FirstOrDefaultAsync(o => o.SubscriptionId == request.SubscriptionId
                                      && o.GroupId == request.GroupId);

        // Inherit is the absence of an override, so store nothing rather than a row that does nothing
        // — otherwise the routing list would have to explain a row that changes no behaviour.
        if (request.AlertMode == RetryAlertMode.Inherit)
        {
            if (existing != null) dbContext.Remove(existing);
            await dbContext.SaveChangesAsync();
            return null;
        }

        // A masked secret has to be restored from whichever level the caller was shown it at. Usage
        // masks two things for a pair: the override's own properties, and the properties of the level
        // it currently inherits from. Overriding an inherited alert starts from the second — there is
        // no override row yet — so both are offered here, with the override's own winning.
        var group = policy.Groups.First(g => g.Id == request.GroupId);
        var inherited = RetryAlertResolver.Resolve(existing, group, policy);

        var restoreFrom = new Dictionary<string, string>();
        foreach (var kv in inherited?.HandlerProperties ?? new Dictionary<string, string>())
            restoreFrom[kv.Key] = kv.Value;
        foreach (var kv in existing?.AlertHandlerProperties ?? new Dictionary<string, string>())
            restoreFrom[kv.Key] = kv.Value;

        var restoreFromHandler = existing?.AlertHandlerId ?? inherited?.HandlerId;
        var properties = AdapterSecretProperties.Merge(restoreFrom, request.AlertHandlerProperties,
            AdapterSecretProperties.MayKeepStoredSecrets(restoreFromHandler, request.AlertHandlerId, restoreFrom,
                request.AlertHandlerProperties ?? new Dictionary<string, string>()));

        if (existing == null)
        {
            dbContext.Add(new RetryAlertOverride
            {
                SubscriptionId = request.SubscriptionId,
                GroupId = request.GroupId,
                AlertMode = request.AlertMode,
                AlertHandlerId = request.AlertHandlerId,
                AlertHandlerProperties = properties
            });
        }
        else
        {
            existing.AlertMode = request.AlertMode;
            existing.AlertHandlerId = request.AlertHandlerId;
            existing.AlertHandlerProperties = properties;
        }

        await dbContext.SaveChangesAsync();
        return null;
    }
}
