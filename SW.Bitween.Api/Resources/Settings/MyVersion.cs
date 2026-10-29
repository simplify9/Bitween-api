using System.Threading.Tasks;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Settings;

/// <summary>
/// This Bitween's release, as a version object, the shape it always had. It used to be the assembly's own
/// version, which every build says is 1.0.0.0; it is now the release, as the About page shows it.
/// </summary>
[HandlerName("myversion")]
public class MyVersion(BitweenOptions options) : IQueryHandler<object>
{
    public Task<object> Handle() =>
        Task.FromResult<object>(new
        {
            BitweenApiVersion = BitweenInfo.Version(options),
        });
}
