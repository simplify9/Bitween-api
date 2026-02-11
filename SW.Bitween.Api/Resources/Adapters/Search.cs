using System.Collections.Generic;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using System.Linq;
using SW.Bitween.Model;

namespace SW.Bitween.Resources.Adapters
{
    public class Search : IQueryHandler<AdapterSearchRequest,object>
    {
        private readonly ServerlessOptions _serverlessOptions;
        private readonly ICloudFilesService _cloudFilesService;
        private readonly NativeAdapterDiscoveryService _nativeAdapterDiscovery;

        public Search(ServerlessOptions serverlessOptions, ICloudFilesService cloudFilesService,
            NativeAdapterDiscoveryService nativeAdapterDiscovery)
        {
            _serverlessOptions = serverlessOptions;
            _cloudFilesService = cloudFilesService;
            _nativeAdapterDiscovery = nativeAdapterDiscovery;
        }


        public async Task<object> Handle(AdapterSearchRequest request)
        {
            // Get native adapters first
            var nativeAdapters = _nativeAdapterDiscovery.GetNativeAdapters(request.Prefix).ToList();

            // Get external adapters from storage
            var cloudFilesList =
                (await _cloudFilesService.ListAsync(
                    $"{_serverlessOptions.AdapterRemotePath}/infolink6.{request.Prefix}"))
                .Where(item => item.Size > 0)
                .Select(i =>
                {
                    var lastSection = i.Key.Split("/").Last();
                    var isSemver = Semver.IsVersionNumber(lastSection);
                    var key = isSemver ? i.Key.Split("/").ElementAt(^2) : lastSection;

                    return key;
                })
                .Distinct()
                .ToList();

            // Combine native (first) and external adapters
            var allAdapters = nativeAdapters.Concat(cloudFilesList);

            return allAdapters.ToDictionary(k => k, v => v);
        }
    }
}