using System;
using System.Collections.Generic;
using System.Linq;

namespace SW.Bitween;

public static class StartupValuesFiller
{
    //{{partner.XY}} => input["XY"]
    public static Dictionary<string, string> Fill(this IDictionary<string, string> inputTemplated,
        Dictionary<string, string> input, string variableNamePrefix)
    {
        var result = new Dictionary<string, string>();
        var prefix = $"{{{{{variableNamePrefix}."; // {{partner.
        
        foreach (var kvp in inputTemplated)
        {
            var value = kvp.Value;
            
            // Check if value is a template like {{partner.XY}}
            if (value != null && value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && value.EndsWith("}}"))
            {
                // Extract the variable name (e.g., "XY" from "{{partner.XY}}")
                var variableName = value.Substring(prefix.Length, value.Length - prefix.Length - 2);
                
                // Look up in input dictionary (case-insensitive)
                var inputValue = input.FirstOrDefault(i => 
                    i.Key.Equals(variableName, StringComparison.OrdinalIgnoreCase)).Value;
                
                result[kvp.Key] = inputValue ?? value; // Use original if not found
            }
            else
            {
                // Keep the original value if it's not a template
                result[kvp.Key] = value;
            }
        }
        
        return result;
    }
}