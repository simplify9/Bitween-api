using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using SW.Bitween.Model;

namespace SW.Bitween.Domain
{
    public class Document : BaseEntity
    {
        public const int AggregationDocumentId = 10001;

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

        /// <summary>Production creation path — Id is database-generated. Code is optional.</summary>
        public Document(string code, string name, DocumentFormat format)
        {
            Code = code;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            PromotedProperties = new Dictionary<string, string>();
            DocumentFormat = format;
        }

        public string Name { get; private set; }
        public string Code { get; private set; }
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

        public void SetName(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public void SetCode(string code)
        {
            Code = code;
        }
    }
}