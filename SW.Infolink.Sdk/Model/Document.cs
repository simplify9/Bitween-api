using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Infolink.Model
{

    public class DocumentCreate : IName
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class DocumentUpdate : DocumentCreate
    {
        public bool BusEnabled { get; set; }
        public string BusMessageTypeName { get; set; }
        public int DuplicateInterval { get; set; }
        //public IDictionary<string, string> PromotedProperties { get; set; }
        public ICollection<KeyAndValue> PromotedProperties { get; set; }

    }

    public class DocumentRow : DocumentUpdate
    {

    }
}
