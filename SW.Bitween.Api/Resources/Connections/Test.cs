using System.Threading.Tasks;
using SW.Bitween.Model;
using SW.Bitween.Services.Connections;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Connections;

/// <summary>Renews the token now and shows how it went: Connected, or the provider's reason.</summary>
[HandlerName("test")]
public class Test(BitweenDbContext dbContext, RequestContext requestContext, ConnectionTokens tokens)
    : ICommandHandler<int, ConnectionTest, ConnectionRow>
{
    public async Task<ConnectionRow> Handle(int key, ConnectionTest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.GlobalValues.Edit);
        return ConnectionRows.Row(await tokens.TestAsync(key));
    }
}
