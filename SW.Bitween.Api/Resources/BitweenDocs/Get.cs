using System.Threading.Tasks;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.BitweenDocs;

/// <summary>
/// An exchange file's content, for the exchange drawer. Signed-in members with the exchanges view
/// only, and only keys that are exactly one of an exchange's files — it used to read any key in the
/// bucket for anyone.
/// </summary>
public class Get(BitweenDbContext dbContext, RequestContext requestContext, XchangeService xchangeService)
    : IQueryHandler<GetBitweenDocModel, object>
{
    public async Task<object> Handle(GetBitweenDocModel request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Exchanges.View);

        var owner = await xchangeService.FindFileOwner(request.DocumentKey)
                    ?? throw new SWNotFoundException(request.DocumentKey);

        return new
        {
            Data = await xchangeService.GetFile(owner.Xchange, owner.Type),
            Key = request.DocumentKey,
        };
    }
}
