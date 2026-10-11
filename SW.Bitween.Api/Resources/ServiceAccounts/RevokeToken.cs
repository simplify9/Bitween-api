using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.Bitween.Resources.Accounts;
using SW.HttpExtensions;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.ServiceAccounts;

/// <summary>
/// Ends one token. Sessions it was already traded for run out on their own within minutes; to end
/// those at once too, disable the account.
/// </summary>
[HandlerName("revokeToken")]
public class RevokeToken(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, RevokeServiceTokenModel, object>
{
    public async Task<object> Handle(int key, RevokeServiceTokenModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.ServiceAccounts.Edit);

        await ServiceAccountStore.Load(dbContext, key);
        await GrantLimits.EnsureCallerOutranks(dbContext, requestContext, key);

        var token = await dbContext.Set<ServiceToken>().SingleOrDefaultAsync(t => t.Id == request.TokenId && t.AccountId == key)
                    ?? throw new SWValidationException("TOKEN_NOT_FOUND", $"This service account has no token with the id {request.TokenId}");
        token.Revoke(requestContext.GetNameIdentifier());
        await dbContext.SaveChangesAsync();
        return null;
    }
}
