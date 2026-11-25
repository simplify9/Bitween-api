using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Partners;

/// <summary>
/// Whether what this partner's people change in the portal waits for our team's approval. On its own
/// rather than in the partner's update, so a client sending an older partner shape can't switch it off.
/// Changes already waiting keep waiting either way.
/// </summary>
[HandlerName("approval")]
public class SetApproval(BitweenDbContext dbContext, RequestContext requestContext)
    : ICommandHandler<int, PartnerApprovalUpdate, object>
{
    public async Task<object> Handle(int key, PartnerApprovalUpdate request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Partners.Edit);
        var partner = await dbContext.FindAsync<Partner>(key) ?? throw new SWNotFoundException(key.ToString());
        partner.ChangesNeedApproval = request.ChangesNeedApproval;
        await dbContext.SaveChangesAsync();
        return null;
    }
}
