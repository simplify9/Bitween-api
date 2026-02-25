using System;
using System.Collections.Generic;
using System.Linq;
using SW.Bitween.Domain;

namespace SW.Bitween;

public static class StartupValuesFiller
{

    public static Dictionary<string, string> Fill(this IDictionary<string, string> inputTemplated,
        Partner partner, GlobalAdapterValuesSet[] globals)
    {
        // First fill globals templates
        var afterGlobals = inputTemplated.Fill(globals ?? []);
        
        // Then fill partner templates using AdapterProperties
        var result = afterGlobals.Fill(partner.AdapterProperties ?? new Dictionary<string, string>(), Partner.TemplateVariableNamePrefix);
        
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
            // Look up in input dictionary (case-insensitive)
            return input.FirstOrDefault(i => 
                i.Key.Equals(content, StringComparison.OrdinalIgnoreCase)).Value;
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
                g.Id.Equals(globalId, StringComparison.OrdinalIgnoreCase));
            
            if (globalSet == null)
            {
                return null; // Keep original if global set not found
            }
            
            // Look up the key in the Values dictionary (case-insensitive)
            return globalSet.Values.FirstOrDefault(v => 
                v.Key.Equals(keyName, StringComparison.OrdinalIgnoreCase)).Value;
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