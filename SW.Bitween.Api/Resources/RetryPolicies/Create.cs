using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

public class Create(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<RetryPolicyCreate, object>
{
    public async Task<object> Handle(RetryPolicyCreate model)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.RetryPolicies.Create);
        RetryGroupValidation.EnsureCanFire(model.Groups);
        RetryGroupValidation.EnsureAlertTransportIsSecure(
            model.AlertHandlerId, model.AlertHandlerProperties);

        // A new policy has nothing stored behind a sentinel, so any that arrives - from a policy
        // copied out of Get, say - is dropped rather than saved as the literal password.
        foreach (var group in model.Groups ?? [])
            AdapterSecretProperties.MergeInPlace(null, group.AlertHandlerProperties);

        var entity = new RetryPolicy
        {
            Name = model.Name,
            Groups = model.Groups ?? [],
            AlertHandlerId = model.AlertHandlerId,
            AlertHandlerProperties = AdapterSecretProperties.Merge(null, model.AlertHandlerProperties)
        };
        dbContext.Add(entity);
        await dbContext.SaveChangesAsync();
        return entity.Id;
    }
}
