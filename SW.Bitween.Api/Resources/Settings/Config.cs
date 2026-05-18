using System.Threading.Tasks;
using SW.Bitween.Services;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Settings;

[Unprotect]
[HandlerName("Config")]
public class Config : IQueryHandler<object>
{
    private readonly BitweenOptions _BitweenOptions;
    private readonly ThemeOptions _themeOptions;
    public Config(BitweenOptions BitweenOptions, ThemeOptions themeOptions)
    {
        _BitweenOptions = BitweenOptions;
        _themeOptions = themeOptions;
    }

    public async Task<object> Handle()
    {
        return new
        {
            _BitweenOptions.MsalClientId,
            _BitweenOptions.MsalRedirectUri,
            _BitweenOptions.MsalTenantId,
            IsRabbitMqManagementConfigured = !string.IsNullOrWhiteSpace(_BitweenOptions.RabbitMqManagementUrl)
                                             && !string.IsNullOrWhiteSpace(_BitweenOptions.RabbitMqManagementUsername)
                                             && !string.IsNullOrWhiteSpace(_BitweenOptions.RabbitMqManagementPassword),
            Theme = _themeOptions
        };
    }
}
