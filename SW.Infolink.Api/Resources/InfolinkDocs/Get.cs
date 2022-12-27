using System.IO;
using System.Threading.Tasks;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.InfolinkDocs;

[Unprotect]
public class Get : IQueryHandler<GetInfolinkDocModel>
{
    private readonly ICloudFilesService cloudFiles;

    public Get(ICloudFilesService cloudFiles)
    {
        this.cloudFiles = cloudFiles;
    }

    public async Task<object> Handle(GetInfolinkDocModel request)
    {
        var stream = await cloudFiles.OpenReadAsync(request.DocumentKey);
        var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();

        return new
        {
            Data = text,
            Key = request.DocumentKey,
        };
    }
}