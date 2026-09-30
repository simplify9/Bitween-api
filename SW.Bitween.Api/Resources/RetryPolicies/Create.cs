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
        await RetryGroupValidation.EnsureAlertChannelsExist(dbContext,
            [model.AlertChannelId, ..RetryGroupValidation.AlertChannelIds(model.Groups)]);

        var entity = new RetryPolicy
        {
            Name = model.Name,
            Groups = model.Groups ?? [],
            AlertChannelId = model.AlertChannelId
        };
        dbContext.Add(entity);
        await dbContext.SaveChangesAsync();
        return entity.Id;
    }
}
