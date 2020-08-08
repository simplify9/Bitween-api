using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Blobs
{
    class GetContent : IQueryHandler<XchangeBlobDto>
    {
        private readonly InfolinkDbContext dbContext;

        public GetContent(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }
        public async Task<object> Handle(XchangeBlobDto request)
        {
            var c = (await dbContext.Set<XchangeBlob>().FindAsync(request.Id,(XchangeFileType) request.Type));

            using (var ms = new MemoryStream(c.Content)) // File.Create(file))
            using (var zips = new GZipStream(ms, CompressionMode.Decompress))
            using (var reader = new BinaryReader(zips))
            {
                return new XchangeBlobDto { Content = reader.ReadString() };
            };
        }
    }

    public class XchangeBlobDto
    {

        public int Id { get; set; }
        public int Type { get; set; }
        public string Content { get; set; }

    }
}
