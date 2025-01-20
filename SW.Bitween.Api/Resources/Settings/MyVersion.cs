using System.Threading.Tasks;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Settings;

[HandlerName("myversion")]
public class MyVersion: IQueryHandler<object>
{
    public Task<object> Handle()
    {
        var version = this.GetType().Assembly.GetName().Version;
        
        return Task.FromResult<object>(new
        {
            BitweenApiVersion = version
        });
    }
}