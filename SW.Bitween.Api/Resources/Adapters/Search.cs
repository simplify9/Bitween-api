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

        public Search(ServerlessOptions serverlessOptions, ICloudFilesService cloudFilesService)
        {
            _serverlessOptions = serverlessOptions;
            _cloudFilesService = cloudFilesService;
        }


        public async Task<object> Handle(AdapterSearchRequest request)
        {

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
                .ToList();


            return cloudFilesList.Distinct().ToDictionary(k => k, v => v);
        }
    }
}