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

        public SearchVersioned(ServerlessOptions serverlessOptions, ICloudFilesService cloudFilesService)
        {
            _serverlessOptions = serverlessOptions;
            _cloudFilesService = cloudFilesService;
        }


        public async Task<object> Handle(AdapterSearchRequest request)
        {
            var index = _serverlessOptions.AdapterRemotePath.Length + 1;

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

            return grouped.Select(i => new
            {
                i.Key,
                Versions = i.Where(v => v.Key != i.Key && Semver.IsVersionNumber(v.Key.Split("/").Last()))
                    .Select(v => new
                    {
                        Key = v.Key[index..]
                    }).ToList()
            });
        }
    }
}