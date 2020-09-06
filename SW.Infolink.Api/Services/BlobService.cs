using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System.IO;
using System.Threading.Tasks;

namespace SW.Infolink
{
    internal class BlobService
    {
        private readonly ICloudFilesService cloudFiles;
        private readonly InfolinkSettings infolinkSettings;

        public BlobService(ICloudFilesService cloudFiles, InfolinkSettings infolinkSettings)
        {
            this.cloudFiles = cloudFiles;
            this.infolinkSettings = infolinkSettings;
        }

        public async Task AddFile(string xchangeId, XchangeFileType type, XchangeFile file)
        {
            await cloudFiles.WriteTextAsync(file.Data, new WriteFileSettings
            {
                //ContentType = "",
                Public = true,
                Key = $"{infolinkSettings.DocumentPrefix}/{xchangeId}/{type.ToString().ToLower()}"
            });
        }

        public async Task<string> GetFile(string xchangeId, XchangeFileType type)
        {
            using var cloudStream = await cloudFiles.OpenReadAsync($"{infolinkSettings.DocumentPrefix}/{xchangeId}/{type.ToString().ToLower()}");
            using var reader = new StreamReader(cloudStream);
            return await reader.ReadToEndAsync();
        }
    }
}
