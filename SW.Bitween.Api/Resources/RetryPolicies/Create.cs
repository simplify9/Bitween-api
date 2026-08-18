using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.RetryPolicies;

public class Create : ICommandHandler<RetryPolicyCreate, object>
{
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Create(BitweenDbContext dbContext, RequestContext requestContext)
    {
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(RetryPolicyCreate model)
    {
        _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);
        RetryGroupValidation.EnsureCanFire(model.Groups);
        RetryGroupValidation.EnsureAlertTransportIsSecure(
            model.AlertHandlerId, model.AlertHandlerProperties);

        // A new policy has nothing stored behind a sentinel, so any that arrives — from a policy
        // copied out of Get, say — is dropped rather than saved as the literal password.
        foreach (var group in model.Groups ?? [])
            AdapterSecretProperties.MergeInPlace(null, group.AlertHandlerProperties);

        var entity = new RetryPolicy
        {
            Name = model.Name,
            Groups = model.Groups ?? [],
            AlertHandlerId = model.AlertHandlerId,
            AlertHandlerProperties = AdapterSecretProperties.Merge(null, model.AlertHandlerProperties)
        };
        _dbContext.Add(entity);
        await _dbContext.SaveChangesAsync();
        return entity.Id;
    }
}
