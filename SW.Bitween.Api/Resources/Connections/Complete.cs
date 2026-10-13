using System.Threading.Tasks;
using SW.Bitween.Model;
using SW.Bitween.Services.Connections;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Connections;

/// <summary>Finishes an approval with what the provider sent the browser back to <c>/oauth-callback</c> with.</summary>
[HandlerName("complete")]
public class Complete(BitweenDbContext dbContext, RequestContext requestContext, ConnectionTokens tokens)
    : ICommandHandler<ConnectionComplete, object>
{
    public async Task<object> Handle(ConnectionComplete request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.GlobalValues.Edit);
        var actor = requestContext.User?.Identity?.Name ?? requestContext.GetNameIdentifier();
        var (id, name) = await tokens.CompleteAuthorizationAsync(request.Code, request.State, actor);
        return new { Id = id, Name = name };
    }
}
