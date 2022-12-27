using System;
using System.Linq;
using SW.PrimitiveTypes;

namespace SW.Infolink;

public static class SearchyExtensions
{
    public static void DatesToUtc(this SearchyRequest searchyRequest)
    {
        foreach (var filter in searchyRequest.Conditions.SelectMany(i => i.Filters))
        {
            if (DateTime.TryParse(filter.Value.ToString(), out var dateTime))
            {
                filter.ValueDateTime = dateTime.ToUniversalTime();
            }
        }
    }
}