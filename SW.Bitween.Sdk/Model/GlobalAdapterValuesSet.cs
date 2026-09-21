using SW.PrimitiveTypes;
using System.Collections.Generic;

namespace SW.Bitween.Model
{
    public class GlobalAdapterValuesSetCreate : IName
    {
        /// <summary>Both required; the server rejects a create without them.</summary>
        public string Id { get; set; } = null!;

        public string Name { get; set; } = null!;
        public Dictionary<string, string> Values { get; set; } = new();

        /// <summary>
        /// Which of <see cref="Values"/> are secrets. Masked with the <c>__private__</c> sentinel
        /// on the way out, restored from storage when the sentinel comes back.
        /// </summary>
        public ICollection<string> SecretProperties { get; set; } = [];
    }

    // Id is inherited from GlobalAdapterValuesSetCreate. Redeclaring it here shadowed the base
    // property (CS0108): a write through a base-typed reference set a different slot from the one
    // a serializer read back.
    public class GlobalAdapterValuesSetRow : GlobalAdapterValuesSetUpdate
    {
    }

    public class GlobalAdapterValuesSetUpdate : GlobalAdapterValuesSetCreate
    {
    }

    public class DeleteGlobalAdapterValuesSetModel
    {
    }
}
