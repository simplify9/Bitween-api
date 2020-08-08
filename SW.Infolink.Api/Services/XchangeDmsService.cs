
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace SW.Infolink
{
    public class XchangeDmsService 
    {
        readonly InfolinkDbContext dbCtxt;
        public XchangeDmsService(InfolinkDbContext dbCtxt)
        {
            this.dbCtxt = dbCtxt;
        }

        public async Task AddFile(int xchangeId, XchangeFileType type, XchangeFile file)
        {
            if (file is null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            using (var ms = new MemoryStream()) // File.Create(file))
            using (var zips = new GZipStream(ms, CompressionLevel.Optimal))
            using (var writer = new BinaryWriter(zips))
            {
                writer.Write(file.Data);
                writer.Flush();
                
                var b = new XchangeBlob
                {
                    Id = xchangeId,
                    Type = type,
                    Content = ms.ToArray()
                };

                dbCtxt.Add(b);
                await dbCtxt.SaveChangesAsync();

            };
        }

        public async Task<string> GetFile(int xchangeId, XchangeFileType xchangeBlobType)
        {

            var c = (await dbCtxt.Set<XchangeBlob>().FindAsync(xchangeId, xchangeBlobType));

            using (var ms = new MemoryStream(c.Content)) // File.Create(file))
            using (var zips = new GZipStream(ms, CompressionMode.Decompress))
            using (var reader = new BinaryReader(zips))
            {
                return reader.ReadString();
            };
        }
    }
}
