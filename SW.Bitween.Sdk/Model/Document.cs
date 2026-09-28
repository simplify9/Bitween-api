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
        Xml = 1,

        /// <summary>
        /// Carried, never read: there are no promoted properties, and only subscriptions and
        /// routes without a filter pick its messages up. A label for what the payload is.
        /// </summary>
        Csv = 2,

        /// <summary>
        /// Anything Bitween has no format for — custom adapters send whatever they like. Treated
        /// exactly as <see cref="Csv"/>.
        /// </summary>
        Other = 3
    }

    public class DocumentCreate : IName
    {
        /// <summary>Required; the server rejects a create without it.</summary>
        public string Code { get; set; } = null!;
        public DocumentFormat DocumentFormat { get; set; }
        /// <summary>Required; the server rejects a create without it.</summary>
        public string Name { get; set; } = null!;
        public bool BusEnabled { get; set; }
        /// <summary>Only meaningful when BusEnabled; null otherwise.</summary>
        public string? BusMessageTypeName { get; set; }
        public int DuplicateInterval { get; set; }

        public bool DisregardsUnfilteredMessages { get; set; }

        /// <summary>Carried on create too, so a new type arrives complete rather than
        /// needing a second save before it can be filtered on.</summary>
        /// <summary>
        /// Null leaves the existing promoted properties alone; an empty collection clears them.
        /// </summary>
        public ICollection<KeyAndValue>? PromotedProperties { get; set; }
    }

    public class DocumentUpdate : DocumentCreate
    {
        public int Id { get; set; }
    }

    /// <summary>Empty on purpose — retiring toggles, so there is nothing to send.</summary>
    public class DocumentRetire
    {
    }

    public class DocumentRow : DocumentUpdate
    {
        /// <summary>
        /// When this type was taken out of use, or null while it is still in use. A retired type
        /// keeps answering for the exchanges already recorded against it and is no longer offered
        /// for new work.
        /// </summary>
        public DateTime? RetiredOn { get; set; }

        /// <summary>
        /// How many subscriptions carry this information type. Counted here because the admin UI
        /// shows it in the list: computing it client-side meant downloading every subscription
        /// alongside every page of this list.
        /// </summary>
        public int UsedByCount { get; set; }
    }
}