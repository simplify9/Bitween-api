using SW.PrimitiveTypes;
using System.Collections.Generic;

namespace SW.Bitween.Model
{
    public class GlobalAdapterValuesSetCreate : IName
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public Dictionary<string, string> Values { get; set; }
    }

    public class GlobalAdapterValuesSetRow : GlobalAdapterValuesSetUpdate
    {
        public string Id { get; set; }
    }

    public class GlobalAdapterValuesSetUpdate : GlobalAdapterValuesSetCreate
    {
    }

    public class DeleteGlobalAdapterValuesSetModel
    {
    }
}
