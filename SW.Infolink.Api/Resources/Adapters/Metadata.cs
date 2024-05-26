using System.Linq;
using System.Threading.Tasks;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Adapters;

[HandlerName("Metadata")]
public class Metadata : IGetHandler<string>
{
    private readonly ServerlessOptions _serverlessOptions;
    private readonly ICloudFilesService _cloudFilesService;

    public Metadata(
        ServerlessOptions serverlessOptions,
        ICloudFilesService cloudFilesService
    )
    {
        _serverlessOptions = serverlessOptions;
        _cloudFilesService = cloudFilesService;
    }

    public async Task<object> Handle(string key, bool lookup = false)
    {
        var cloudFilesList =
            await _cloudFilesService.GetMetadataAsync(
                $"{_serverlessOptions.AdapterRemotePath}/{key}"
            );

        return cloudFilesList;
    }
}