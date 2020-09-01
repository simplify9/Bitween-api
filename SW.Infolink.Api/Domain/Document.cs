using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;

namespace SW.Infolink.Domain
{
    public class Document : BaseEntity
    {
        public Document(int id, string name)
        {
            Id = id;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            PromotedProperties = new Dictionary<string, string>();
        }
        public string Name { get; set; }
        public bool BusEnabled { get; set; }
        public string BusMessageTypeName { get; set; }
        public int DuplicateInterval { get; set; }
        public IReadOnlyDictionary<string, string> PromotedProperties { get;  set; }
    }
}
