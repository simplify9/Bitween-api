using System;
using System.Collections.Generic;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using System.Linq;
using SW.Bitween.Model;

namespace SW.Bitween.Resources.Adapters
{
    [HandlerName("Versioned")]
    public class SearchVersioned : IQueryHandler<AdapterSearchRequest,object>
    {
        private readonly ServerlessOptions _serverlessOptions;
        private readonly ICloudFilesService _cloudFilesService;
        private readonly NativeAdapterDiscoveryService _nativeAdapterDiscovery;
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public SearchVersioned(ServerlessOptions serverlessOptions, ICloudFilesService cloudFilesService,
            NativeAdapterDiscoveryService nativeAdapterDiscovery, BitweenDbContext dbContext,
            RequestContext requestContext)
        {
            _serverlessOptions = serverlessOptions;
            _cloudFilesService = cloudFilesService;
            _nativeAdapterDiscovery = nativeAdapterDiscovery;
            _dbContext = dbContext;
            _requestContext = requestContext;
        }


        public async Task<object> Handle(AdapterSearchRequest request)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Subscriptions.View);

            var index = _serverlessOptions.AdapterRemotePath.Length + 1;

            // Get native adapters first (they don't have versions)
            var nativeAdapters = _nativeAdapterDiscovery.GetNativeAdapters(request.Prefix)
                .Select(key => new
                {
                    Key = key,
                    Versions = new List<object>() // Native adapters have no versions
                })
                .ToList();

            // Get external adapters from storage
            var cloudFilesList =
                (await _cloudFilesService.ListAsync(
                    $"{_serverlessOptions.AdapterRemotePath}/infolink6.{request.Prefix}"))
                .Where(item => item.Size > 0)
                .ToList();

            var grouped = cloudFilesList
                .GroupBy(i =>
                {
                    var lastSection = i.Key.Split("/").Last();
                    var isSemver = Semver.IsVersionNumber(lastSection);
                    var key = isSemver ? i.Key.Split("/").ElementAt(^2) : lastSection;

                    return key;
                });

            var externalAdapters = grouped.Select(i => new
            {
                i.Key,
                Versions = i.Where(v => v.Key != i.Key && Semver.IsVersionNumber(v.Key.Split("/").Last()))
                    .Select(v => new
                    {
                        Key = v.Key[index..]
                    }).ToList()
            });

            // Return native adapters first, then external
            return nativeAdapters.Concat<object>(externalAdapters);
        }
    }
}