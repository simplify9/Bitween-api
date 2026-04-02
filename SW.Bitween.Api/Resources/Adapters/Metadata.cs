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

    public Metadata(
        ServerlessOptions serverlessOptions,
        ICloudFilesService cloudFilesService,
        NativeAdapterDiscoveryService nativeAdapterDiscovery
    )
    {
        _serverlessOptions = serverlessOptions;
        _cloudFilesService = cloudFilesService;
        _nativeAdapterDiscovery = nativeAdapterDiscovery;
    }

    public async Task<object> Handle(string key)
    {
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