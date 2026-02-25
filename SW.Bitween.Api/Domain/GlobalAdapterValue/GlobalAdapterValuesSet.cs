using System;
using System.Collections.Generic;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain;

public class GlobalAdapterValuesSet:BaseEntity<string>
{
    public string Name { get; set; }
    public Dictionary<string, string> Values { get; set; }
}