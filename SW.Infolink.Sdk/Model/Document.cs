using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Infolink.Model
{

    public enum DocumentFormat
    {
        Json = 0,
        Xml = 1
    }
    public class DocumentCreate : IName
    {
        public int Id { get; set; }
        public DocumentFormat DocumentFormat { get; set; }
        public string Name { get; set; }
    }

    public class DocumentUpdate : DocumentCreate
    {
        public bool BusEnabled { get; set; }
        public string BusMessageTypeName { get; set; }
        public int DuplicateInterval { get; set; }
        
        public ICollection<KeyAndValue> PromotedProperties { get; set; }
    }

    public class DocumentRow : DocumentUpdate
    {

    }
}
