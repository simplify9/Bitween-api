using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Infolink
{
    public class BlobService
    {
        private readonly ICloudFilesService cloudFiles;
        private readonly InfolinkSettings infolinkSettings;
        private readonly ServerlessOptions serverlessOptions;

        public BlobService(ICloudFilesService cloudFiles, InfolinkSettings infolinkSettings)
        {
            this.cloudFiles = cloudFiles;
            this.infolinkSettings = infolinkSettings;
        }

        public async Task AddFile(int xchangeId, XchangeFileType type, XchangeFile file)
        {
            await cloudFiles.WriteTextAsync(file.Data, new WriteFileSettings
            {
                //ContentType = "",
                Key = $"{infolinkSettings.DocumentPrefix}/{xchangeId}/{type.ToString().ToLower()}"
            });
        }

        public async Task<string> GetFile(int xchangeId, XchangeFileType type)
        {

            //var c = (await dbCtxt.Set<XchangeBlob>().FindAsync(xchangeId, xchangeBlobType));

            //using (var ms = new MemoryStream(c.Content)) // File.Create(file))
            //using (var zips = new GZipStream(ms, CompressionMode.Decompress))
            //using (var reader = new BinaryReader(zips))
            //{
            //    return reader.ReadString();
            //};

            using var cloudStream = await cloudFiles.OpenReadAsync($"{infolinkSettings.DocumentPrefix}/{xchangeId}/{type.ToString().ToLower()}");
            using var reader = new StreamReader(cloudStream);
            return await reader.ReadToEndAsync();
        }


    }
}
