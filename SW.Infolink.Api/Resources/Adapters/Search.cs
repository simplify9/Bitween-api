using SW.PrimitiveTypes;
using System.Threading.Tasks;
using System.Linq;
using SW.Infolink.Model;

namespace SW.Infolink.Resources.Adapters
{
    public class Search : IQueryHandler<AdapterSearchRequest>
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
            var index = _serverlessOptions.AdapterRemotePath.Length + 1;

            var cloudFilesList =
                (await _cloudFilesService.ListAsync(
                    $"{_serverlessOptions.AdapterRemotePath}/infolink6.{request.Prefix}"))
                .Where(item => item.Size > 0).ToList();

            return cloudFilesList.ToDictionary(k => k.Key[index..], v => v.Key[index..]);
        }
    }
}