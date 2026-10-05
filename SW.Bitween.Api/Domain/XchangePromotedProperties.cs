using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Collections.Generic;
using System.Linq;

namespace SW.Bitween.Domain
{
    public class XchangePromotedProperties : BaseEntity<string>
    {
        /// <summary>
        /// The most a promoted value is stored as. A longer one is cut, ending in "…" to show it.
        /// </summary>
        /// <remarks>
        /// Promoted values find and name an exchange — an order number, a customer — and they are
        /// shown on the Exchanges page and name its archived files. A path that picks up a notes
        /// field or a whole object would put thousands of characters in all of those. Routing is
        /// not affected: match expressions read the payload, not these.
        /// </remarks>
        public const int MaxValueLength = 500;

        private XchangePromotedProperties()
        {
        }

        public XchangePromotedProperties(string xchangeId, FilterResult filterResult)
        {
            Id = xchangeId;
            Properties = filterResult.Properties.ToDictionary(p => p.Key, p => Cut(p.Value));
            Hits = filterResult.Hits?.ToArray() ?? new int[] { };
            
            
            foreach (var (key, value) in Properties)
            {
                var val = $"{key}:{value}";
                if (string.IsNullOrEmpty(PropertiesRaw)) PropertiesRaw = val;
                else PropertiesRaw += $",{val}";
            }
        }

        /// <summary><paramref name="value"/> as it is stored: whole, or cut to <see cref="MaxValueLength"/> and "…".</summary>
        public static string Cut(string value)
        {
            if (value is null || value.Length <= MaxValueLength) return value;

            // Never between the two halves of a character outside the basic plane, an emoji say:
            // half of one on its own is not valid text.
            var length = char.IsHighSurrogate(value[MaxValueLength - 1]) ? MaxValueLength - 1 : MaxValueLength;
            return value[..length] + "…";
        }

        public IReadOnlyDictionary<string, string> Properties { get; private set; }
        public string PropertiesRaw { get; private set; }
        public int[] Hits { get; private set; }

    }
}
