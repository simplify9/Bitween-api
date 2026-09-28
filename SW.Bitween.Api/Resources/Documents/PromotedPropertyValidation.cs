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

            // Nothing reads these formats, so a property on one would save cleanly and then
            // never hold a value.
            if (format is not (DocumentFormat.Json or DocumentFormat.Xml) && promotedProperties.Count > 0)
                throw new SWValidationException("PROMOTED_PROPERTIES_NOT_SUPPORTED",
                    $"{format} information types can't have promoted properties: Bitween doesn't read their content. Remove them first.");

            foreach (var pp in promotedProperties)
            {
                if (string.IsNullOrWhiteSpace(pp.Key))
                    throw new SWValidationException("INVALID_PROMOTED_PROPERTY_KEY",
                        "Promoted property key cannot be null or empty.");

                if (string.IsNullOrWhiteSpace(pp.Value))
                    throw new SWValidationException("INVALID_PROMOTED_PROPERTY_VALUE",
                        $"Promoted property '{pp.Key}' must have a non-empty path value.");

                // Trimmed here *and* written back. Validating the trimmed value while storing the
                // raw one let " $.ref" pass every check below and then fail at read time, which
                // 400s every message on this information type rather than this one save.
                var trimmed = pp.Value.Trim();
                pp.Value = trimmed;

                if (format == DocumentFormat.Json)
                {
                    // Must be a JSONPath: starts with '$' or a simple dot-separated identifier path
                    //
                    // The '$' branch is checked rather than trusted. Treating any leading '$' as
                    // proof of a JSONPath let "$", "$." and "$.." through, none of which select
                    // anything — they saved cleanly and failed only once a message arrived. A step
                    // is a '.'/'..' followed by a name, or a bracket; names stay deliberately
                    // permissive (anything but a delimiter) so paths that already work keep working.
                    var valid = trimmed.StartsWith("$")
                        ? Regex.IsMatch(trimmed, @"^\$(?:\.\.?[^.\[\]]+|\[[^\]]+\])+$")
                        : Regex.IsMatch(trimmed, @"^[a-zA-Z_][a-zA-Z0-9_]*(?:(\.[a-zA-Z_][a-zA-Z0-9_]*)|(\[[0-9]+\]))*$");

                    if (!valid)
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
