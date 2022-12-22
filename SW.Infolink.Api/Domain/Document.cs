using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using SW.Infolink.Model;

namespace SW.Infolink.Domain
{
    public class Document : BaseEntity
    {
        public const int AggregationDocumentId = 10001;
        //public const int AggregationDocumentId = 10001;
        //public const int AggregationDocumentId = 10001;

        public Document(int id, string name)
        {
            Id = id;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            PromotedProperties = new Dictionary<string, string>();
        }
        public Document(int id, string name, DocumentFormat format)
        {
            Id = id;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            PromotedProperties = new Dictionary<string, string>();
            DocumentFormat = format;
        }
        public string Name { get; private set; }
        public bool BusEnabled { get; set; }
        public string BusMessageTypeName { get; set; }
        public int DuplicateInterval { get; set; }
        public bool? DisregardsUnfilteredMessages { get; set; }
        
        public DocumentFormat DocumentFormat { get; set; }
        public IReadOnlyDictionary<string, string> PromotedProperties { get; private set; }

        public void SetDictionaries(IReadOnlyDictionary<string, string> promotedProperties)
        {
            PromotedProperties = promotedProperties;
        }
    }
}
