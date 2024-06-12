using System.Threading.Tasks;
using SW.Infolink.Services;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Settings;

[Unprotect]
[HandlerName("Config")]
public class Config : IQueryHandler
{
    private readonly InfolinkOptions _infolinkOptions;
    private readonly ThemeOptions _themeOptions;

    public Config(InfolinkOptions infolinkOptions, ThemeOptions themeOptions)
    {
        _infolinkOptions = infolinkOptions;
        _themeOptions = themeOptions;
    }

    public async Task<object> Handle()
    {
        return new
        {
            _infolinkOptions.MsalClientId,
            _infolinkOptions.MsalRedirectUri,
            _infolinkOptions.MsalTenantId,
            Theme = _themeOptions
        };
    }
}