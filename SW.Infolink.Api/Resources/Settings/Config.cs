using System.Threading.Tasks;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Settings;

[Unprotect]
[HandlerName("Config")]
public class Config : IQueryHandler
{
    private readonly InfolinkOptions _infolinkOptions;

    public Config(InfolinkOptions infolinkOptions)
    {
        _infolinkOptions = infolinkOptions;
    }

    public async Task<object> Handle()
    {
        return new
        {
            _infolinkOptions.MsalClientId,
            _infolinkOptions.MsalRedirectUri
        };
    }
}