using System;
using System.Collections.Generic;
using System.Linq;

namespace SW.Infolink;

public class NotOneOfSpec : IPropertyMatchSpecification
{
    public NotOneOfSpec(string path, IEnumerable<string> values)
    {
        Path = path;
        Values = values.ToArray();
    }
    
    public string Path { get; private set; }

    public string[] Values { get; private set; }
    
    public bool IsMatch(IPropertyReader reader)
    {
        reader.TryGetValue(Path, out var value);
        
        return !Values.Any(i => i.Equals(value, StringComparison.InvariantCultureIgnoreCase));
    }
    
    public string Name => "not_one_of";
    
    public override string ToString()
    {
        return $"{Path} is not one of [{string.Join(",", Values)}]";
    }
}