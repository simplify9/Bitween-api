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

        /// <summary>
        /// When this type was taken out of use, or null while it is still in use.
        /// </summary>
        /// <remarks>
        /// Retiring is what a type that has been used wants instead of deleting: exchanges name
        /// the type they carried, so removing the row to get it out of a picker would either take
        /// that history with it or leave it anonymous. A retired type keeps answering for every
        /// exchange already recorded against it, and simply stops being offered for new work.
        /// </remarks>
        public DateTime? RetiredOn { get; private set; }

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

        /// <summary>
        /// JSON Schema for a JSON type, XSD for an XML one; null checks nothing. What arrives at a
        /// gateway that doesn't match is refused, and an exchange whose input doesn't match fails.
        /// A private setter so a save that doesn't mention it leaves it alone.
        /// </summary>
        public string ValidationSchema { get; private set; }

        public void SetValidationSchema(string schema)
        {
            ValidationSchema = string.IsNullOrWhiteSpace(schema) ? null : schema;
        }

        public void Retire()
        {
            RetiredOn = DateTime.UtcNow;
        }

        public void Restore()
        {
            RetiredOn = null;
        }
    }
}