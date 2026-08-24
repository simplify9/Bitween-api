using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Documents
{
    /// <summary>
    /// The rules a promoted property has to satisfy, shared by Create and Update so
    /// the two paths cannot drift apart on what a valid path is.
    /// </summary>
    public static class PromotedPropertyValidation
    {
        public static void Check(ICollection<KeyAndValue> promotedProperties, DocumentFormat format)
        {
            if (promotedProperties == null) return;

            foreach (var pp in promotedProperties)
            {
                if (string.IsNullOrWhiteSpace(pp.Key))
                    throw new SWValidationException("INVALID_PROMOTED_PROPERTY_KEY",
                        "Promoted property key cannot be null or empty.");

                if (string.IsNullOrWhiteSpace(pp.Value))
                    throw new SWValidationException("INVALID_PROMOTED_PROPERTY_VALUE",
                        $"Promoted property '{pp.Key}' must have a non-empty path value.");

                var trimmed = pp.Value.Trim();

                if (format == DocumentFormat.Json)
                {
                    // Must be a JSONPath: starts with '$' or a simple dot-separated identifier path
                    if (!trimmed.StartsWith("$") && !Regex.IsMatch(trimmed, @"^[a-zA-Z_][a-zA-Z0-9_]*(?:(\.[a-zA-Z_][a-zA-Z0-9_]*)|(\[[0-9]+\]))*$"))
                        throw new SWValidationException("INVALID_PROMOTED_PROPERTY_PATH",
                            $"Promoted property '{pp.Key}' has an invalid JSON path: '{pp.Value}'. Expected a JSONPath expression (e.g. '$.field.subField') or dot-notation path.");
                }
                else if (format == DocumentFormat.Xml)
                {
                    // Basic XPath sanity: must start with '/' or '//' or be a valid element path
                    if (!trimmed.StartsWith("/") && !Regex.IsMatch(trimmed, @"^[a-zA-Z_][a-zA-Z0-9_/\[\]@.:*-]*$"))
                        throw new SWValidationException("INVALID_PROMOTED_PROPERTY_PATH",
                            $"Promoted property '{pp.Key}' has an invalid XML path: '{pp.Value}'. Expected an XPath expression (e.g. '/root/element').");
                }
            }

            var duplicateKey = promotedProperties
                .GroupBy(pp => pp.Key, System.StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1)?.Key;

            if (duplicateKey != null)
                throw new SWValidationException("DUPLICATE_PROMOTED_PROPERTY_KEY",
                    $"Promoted property key '{duplicateKey}' appears more than once.");
        }
    }
}
