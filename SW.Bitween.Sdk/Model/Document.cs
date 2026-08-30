using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween.Model
{
    public enum DocumentFormat
    {
        Json = 0,
        Xml = 1
    }

    public class DocumentCreate : IName
    {
        public string Code { get; set; }
        public DocumentFormat DocumentFormat { get; set; }
        public string Name { get; set; }
        public bool BusEnabled { get; set; }
        public string BusMessageTypeName { get; set; }
        public int DuplicateInterval { get; set; }

        public bool DisregardsUnfilteredMessages { get; set; }

        /// <summary>Carried on create too, so a new type arrives complete rather than
        /// needing a second save before it can be filtered on.</summary>
        public ICollection<KeyAndValue> PromotedProperties { get; set; }
    }

    public class SearchDocumentTrailModel
    {
        public int? Limit { get; set; }
        public int? Offset { get; set; }
        public int DocumentId { get; set; }
    }

    public class DocumentTrailModel : TrailBaseModel
    {
        public int DocumentId { get; set; }
    }

    public class DocumentUpdate : DocumentCreate
    {
        public int Id { get; set; }
    }

    public class DocumentRow : DocumentUpdate
    {
        /// <summary>
        /// How many subscriptions carry this information type. Counted here because the admin UI
        /// shows it in the list: computing it client-side meant downloading every subscription
        /// alongside every page of this list.
        /// </summary>
        public int UsedByCount { get; set; }
    }
}