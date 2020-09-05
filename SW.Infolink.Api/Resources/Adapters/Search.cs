using SW.PrimitiveTypes;
using System.Threading.Tasks;
using System.Linq;
using SW.Infolink.Model;

namespace SW.Infolink.Resources.Adapters
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
            var index = serverlessOptions.AdapterRemotePath.Length + 1;

            var cloudFilesList = (await cloudFilesService.ListAsync($"{serverlessOptions.AdapterRemotePath}/infolink.mappers")).Where(item => item.Size > 0).ToList();

            if (lookup)
                return cloudFilesList.ToDictionary(k => k.Key.Substring(index), v => v.Key.Substring(index));

            var sr = new SearchyResponse<AdapterRow>();
            sr.TotalCount = cloudFilesList.Count();
            sr.Result = cloudFilesList.Select(item => new AdapterRow
            {
                Id = item.Key.Substring(index)
            });

            return sr;

        }
    }
}
