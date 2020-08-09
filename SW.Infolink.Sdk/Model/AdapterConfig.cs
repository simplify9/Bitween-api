using System.Collections.Generic;

namespace SW.Infolink
{
    public class AdapterConfig
    {
        public AdapterType Type { get;set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int Timeout { get; set; }
        public string ServerlessId { get; set; }
        public IDictionary<string, string> Properties { get; set; }
        public string Hash { get; set; }
    }
}
