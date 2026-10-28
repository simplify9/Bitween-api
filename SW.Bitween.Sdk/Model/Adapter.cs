using SW.Bitween;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Bitween.Model
{
    public class AdapterSearchRequest
    {
        /// <summary>Optional filter; null lists every adapter.</summary>
        public string? Prefix { get; set; }

        /// <summary>
        /// False lists the adapters without their startup properties, which for a published adapter
        /// means starting it: for a caller after its versions alone. Null or true describes them.
        /// </summary>
        public bool? StartupValues { get; set; }
    }

    public class AdapterRow
    {
        public string Id { get; set; } = null!;
    }

}
