using System.IO;
using System.Threading.Tasks;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.BitweenDocs;

[Unprotect]
public class Get : IQueryHandler<GetBitweenDocModel,object>
{
    private readonly ICloudFilesService cloudFiles;

    public Get(ICloudFilesService cloudFiles)
    {
        this.cloudFiles = cloudFiles;
    }

    public async Task<object> Handle(GetBitweenDocModel request)
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