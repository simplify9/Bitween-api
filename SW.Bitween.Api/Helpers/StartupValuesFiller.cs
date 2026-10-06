using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SW.Bitween.Domain;

namespace SW.Bitween;

public static class StartupValuesFiller
{
    /// <summary>
    /// How the pipeline tells a runtime that this subscription runs through a data source, without
    /// every call site along the way having to grow a parameter for it.
    ///
    /// Reserved, and stripped before the adapter ever sees it — the adapter's own settings come
    /// from the data source itself. The double-underscore convention matches the
    /// <c>__partner__</c> and <c>__globals__</c> injections the mapper already relies on.
    /// </summary>
    public const string DataSourceIdKey = "__dataSourceId__";

    /// <summary>
    /// Which subscription this invocation is for.
    ///
    /// Unlike <see cref="DataSourceIdKey"/> this is NOT stripped: the adapter reads it. A resident
    /// data source is one instance shared by every subscription pointed at it, so anything the
    /// adapter remembers between calls — a receive cursor above all — has to be namespaced by the
    /// reader, or two subscriptions polling one connection share one cursor and each sees half the
    /// rows. The adapter side of this contract is DbReceiver's CursorStateName.
    /// </summary>
    public const string SubscriptionIdKey = "__subscriptionId__";

    /// <summary>
    /// Stamps the data source id onto a set of adapter properties. A null id leaves them alone, so
    /// every subscription that does not use one is byte-for-byte what it was.
    /// </summary>
    public static Dictionary<string, string> WithDataSource(this Dictionary<string, string> properties,
        int? dataSourceId)
    {
        if (dataSourceId != null) properties[DataSourceIdKey] = dataSourceId.Value.ToString();
        return properties;
    }

    /// <summary>
    /// Stamps the subscription id, so an adapter holding state for several subscriptions can tell
    /// them apart. Unconditional: a receiver that cannot say who it is reading for is exactly the
    /// case that produced a shared cursor.
    /// </summary>
    public static Dictionary<string, string> WithSubscription(this Dictionary<string, string> properties,
        int subscriptionId)
    {
        properties[SubscriptionIdKey] = subscriptionId.ToString();
        return properties;
    }


    /// <summary>The token prefix for a source value: <c>{{source.PATH}}</c>.</summary>
    public const string SourceVariableNamePrefix = "source";

    /// <summary>
    /// The bus value a published response carries the id of the exchange that delivered under, so a
    /// bus gateway route picking it up can read that exchange's input itself.
    /// </summary>
    public const string SourceXchangeBusValue = "bitween-source-xchange";

    private static readonly Regex SourceToken = new(@"\{\{source\.([^}]+)\}\}", RegexOptions.IgnoreCase);

    /// <summary>
    /// Fills <c>{{source.PATH}}</c> from the values read out of the original document: the input of
    /// the exchange whose delivery got the response this one runs on. Null values leave the tokens as they are — every exchange but
    /// a response or bus gateway subscription's — and so does a path with no value, for
    /// <see cref="SourcePathsIn"/> to find.
    /// </summary>
    public static Dictionary<string, string> FillSource(this Dictionary<string, string> properties,
        IReadOnlyDictionary<string, string> sourceValues) =>
        sourceValues == null
            ? properties
            : properties.Fill(new Dictionary<string, string>(sourceValues), SourceVariableNamePrefix);

    /// <summary>The paths of every <c>{{source.PATH}}</c> token in <paramref name="values"/>.</summary>
    public static IEnumerable<string> SourcePathsIn(IEnumerable<string> values) =>
        (values ?? [])
        .Where(v => v != null)
        .SelectMany(v => SourceToken.Matches(v).Select(m => m.Groups[1].Value))
        .Distinct();

    public static Dictionary<string, string> Fill(this IDictionary<string, string> inputTemplated,
        Partner partner, GlobalAdapterValuesSet[] globals)
    {
        if (inputTemplated == null) return new Dictionary<string, string>();

        // First fill globals templates
        var afterGlobals = inputTemplated?.Fill(globals ?? []) ?? new Dictionary<string, string>();
        
        // Then fill partner templates using AdapterProperties
        var adapterProperties = partner?.AdapterProperties ?? new Dictionary<string, string>();
        var result = afterGlobals.Fill(adapterProperties, Partner.TemplateVariableNamePrefix);

        return result;
    }
    //{{partner.XY}} => input["XY"]
    private static Dictionary<string, string> Fill(this IDictionary<string, string> inputTemplated,
        Dictionary<string, string> input, string variableNamePrefix)
    {
        var prefix = $"{{{{{variableNamePrefix}."; // {{partner.
        
        return FillTemplates(inputTemplated, prefix, (content) =>
        {
            // Simple case: extract variable name and look up in input dictionary
            if (input == null) return null;
            // Look up in input dictionary (case-insensitive)
            return input.FirstOrDefault(i => 
                content.Equals(i.Key, StringComparison.OrdinalIgnoreCase)).Value;
        });
    }

    private static Dictionary<string, string> Fill(this IDictionary<string, string> inputTemplated,
        GlobalAdapterValuesSet[] globals)
    {
        var prefix = "{{globals."; // {{globals.
        
        return FillTemplates(inputTemplated, prefix, (content) =>
        {
            // Complex case: split into global ID and key name
            var parts = content.Split('.', 2);
            if (parts.Length != 2)
            {
                return null; // Keep original if format is invalid
            }
            
            var globalId = parts[0];
            var keyName = parts[1];
            
            // Find the matching global adapter values set
            var globalSet = globals.FirstOrDefault(g => 
                globalId.Equals(g?.Id, StringComparison.OrdinalIgnoreCase));

            if (globalSet == null)
            {
                return null; // Keep original if global set not found
            }
            
            // Look up the key in the Values dictionary (case-insensitive)
            return globalSet.Values?.FirstOrDefault(v =>
                keyName.Equals(v.Key, StringComparison.OrdinalIgnoreCase)).Value;
        });
    }
     
    private static Dictionary<string, string> FillTemplates(
        IDictionary<string, string> inputTemplated,
        string prefix,
        Func<string, string> resolver)
    {
        var result = new Dictionary<string, string>();
        
        foreach (var kvp in inputTemplated)
        {
            var value = kvp.Value;
            
            if (value != null && value.Contains(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var sb = new System.Text.StringBuilder(value);
                var searchFrom = 0;
                
                while (true)
                {
                    var current = sb.ToString();
                    var start = current.IndexOf(prefix, searchFrom, StringComparison.OrdinalIgnoreCase);
                    if (start == -1) break;
                    
                    var end = current.IndexOf("}}", start + prefix.Length, StringComparison.Ordinal);
                    if (end == -1) break;
                    
                    var content = current.Substring(start + prefix.Length, end - start - prefix.Length);
                    var resolvedValue = resolver(content);
                    
                    if (resolvedValue != null)
                    {
                        var fullToken = current.Substring(start, end - start + 2);
                        sb.Replace(fullToken, resolvedValue, start, fullToken.Length);
                        searchFrom = start + resolvedValue.Length;
                    }
                    else
                    {
                        // Skip past this token to avoid infinite loop
                        searchFrom = end + 2;
                    }
                }
                
                result[kvp.Key] = sb.ToString();
            }
            else
            {
                result[kvp.Key] = value;
            }
        }
        
        return result;
    }
}