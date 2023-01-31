using System;
using System.Collections.Generic;
using System.Linq;

namespace SW.Infolink.Model;

public class OneOfSpec : IPropertyMatchSpecification
{
    public OneOfSpec(string path, IEnumerable<string> values)
    {
        Path = path;
        Values = values.ToArray();
    }

    public string Path { get; private set; }

    public string[] Values { get; private set; }

    public override string ToString()
    {
        return $"{Path} is one of [{string.Join(",", Values)}]";
    }

    public bool IsMatch(IExchangePayloadReader reader)
    {
        reader.TryGetValue(Path, out var value);
        
        return Values.Any(i => i.Equals(value, StringComparison.InvariantCultureIgnoreCase));
    }
    
    public string Name => "one_of";
}