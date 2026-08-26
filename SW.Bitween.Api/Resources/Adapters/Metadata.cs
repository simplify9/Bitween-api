using System;
using System.Linq;
using System.Threading.Tasks;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Adapters;

[HandlerName("Metadata")]
public class Metadata : IGetHandler<string, object>
{
    private readonly ServerlessOptions _serverlessOptions;
    private readonly ICloudFilesService _cloudFilesService;
    private readonly NativeAdapterDiscoveryService _nativeAdapterDiscovery;
    private readonly BitweenDbContext _dbContext;
    private readonly RequestContext _requestContext;

    public Metadata(
        ServerlessOptions serverlessOptions,
        ICloudFilesService cloudFilesService,
        NativeAdapterDiscoveryService nativeAdapterDiscovery,
        BitweenDbContext dbContext,
        RequestContext requestContext
    )
    {
        _serverlessOptions = serverlessOptions;
        _cloudFilesService = cloudFilesService;
        _nativeAdapterDiscovery = nativeAdapterDiscovery;
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async Task<object> Handle(string key)
    {
        await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Subscriptions.View);

        var decodedKey = Uri.UnescapeDataString(key);

        if (decodedKey.StartsWith(NativeAdapterDiscoveryService.NativePrefix, StringComparison.OrdinalIgnoreCase))
            return new { };

        var cloudFilesList =
            await _cloudFilesService.GetMetadataAsync(
                $"{_serverlessOptions.AdapterRemotePath}/{decodedKey}"
            );

        return cloudFilesList;
    }
}