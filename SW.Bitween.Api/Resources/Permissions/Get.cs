using System.Threading.Tasks;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Permissions;

/// <summary>
/// The permission catalog. Static data — the management UI builds its role matrix from this, so
/// the grants it offers can't drift from what the handlers actually enforce.
/// </summary>
public class Get : IQueryHandler<object>
{
    public Task<object> Handle() => Task.FromResult<object>(PermissionCatalog.Areas);
}
