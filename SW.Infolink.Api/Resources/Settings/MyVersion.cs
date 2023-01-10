using System.Threading.Tasks;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Settings;

[HandlerName("myversion")]
public class MyVersion: IQueryHandler
{
    public Task<object> Handle()
    {
        var version = this.GetType().Assembly.GetName().Version;
        
        return Task.FromResult<object>(new
        {
            InfolinkApiVersion = version
        });
    }
}