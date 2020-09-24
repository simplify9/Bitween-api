using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.Model
{
    public class FilterResult
    {
        public FilterResult()
        {
            Hits = new HashSet<int>();
            Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public HashSet<int> Hits { get; set; }
        public IDictionary<string, string> Properties { get; set; }
    }
}
