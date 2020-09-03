using SW.PrimitiveTypes;
using System.Threading.Tasks;
using System.Linq;
using SW.Infolink.Model;

namespace SW.Infolink.Api.Resources.Adapters
{
    class Search : ISearchyHandler
    {
        private readonly ServerlessOptions serverlessOptions;
        private readonly ICloudFilesService cloudFilesService;

        public Search(ServerlessOptions serverlessOptions, ICloudFilesService cloudFilesService)
        {
            this.serverlessOptions = serverlessOptions;
            this.cloudFilesService = cloudFilesService;
        }

        async public Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {

            var cloudFilesList = (await cloudFilesService.ListAsync($"{serverlessOptions.AdapterRemotePath}")).Where(item => item.Size > 0).ToList();

            if (lookup)
                return cloudFilesList.ToDictionary(k => k.Key, v => v.Key);

            var sr = new SearchyResponse<AdapterRow>();
            sr.TotalCount = cloudFilesList.Count();
            sr.Result = cloudFilesList.Select(item => new AdapterRow
            {
                Id = item.Key.Substring(serverlessOptions.AdapterRemotePath.Length + 1)
            });

            return sr;

        }
    }
}
