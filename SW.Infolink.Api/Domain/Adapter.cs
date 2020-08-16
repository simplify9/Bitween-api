using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace SW.Infolink.Domain
{
    public class Adapter : BaseEntity
    {
        private Adapter()
        {
        }

        public Adapter(AdapterType type, string name, string serverlessId) 
        {
            Type = type;
            Name = name;
            Properties = new Dictionary<string, string>();
            ServerlessId = serverlessId;
        }

        public AdapterType Type { get; private set; }
        public string Name { get; set; }
        //public int DocumentId { get; set; }
        public string Description { get; set; }
        public int Timeout { get; set; }
        public string ServerlessId { get; set; }
        public IReadOnlyDictionary<string, string> Properties { get; set; }
    }
}
