using System.Threading.Tasks;
using SW.Bitween.Model;
using SW.Bitween.Services.Connections;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Connections;

/// <summary>Starts approving an authorization code connection: the provider's page to send the browser to.</summary>
[HandlerName("authorize")]
public class Authorize(BitweenDbContext dbContext, RequestContext requestContext, ConnectionTokens tokens)
    : ICommandHandler<int, ConnectionAuthorize, ConnectionAuthorizeResult>
{
    public async Task<ConnectionAuthorizeResult> Handle(int key, ConnectionAuthorize request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.GlobalValues.Edit);
        return new ConnectionAuthorizeResult { Url = await tokens.StartAuthorizationAsync(key) };
    }
}
