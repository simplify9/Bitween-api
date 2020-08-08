
namespace SW.Infolink.Domain
{
    internal class XchangeBlob
    {
        public int Id { get; set; }
        public XchangeFileType Type { get; set; }
        public byte[] Content { get; set; }
    }
}
