
using System;
using System.Collections.Generic;

namespace SW.Infolink.Model
{
    public class SubscriptionDto 
    {
        public SubscriptionDto()
        {
            Properties = new Dictionary<string, string>();
        }

        public int Id { get; set; }
        public string Name { get; set; }
        public int DocumentId { get; set; }
        public int HandlerId { get; set; }
        public int MapperId { get; set; }
        //public bool Temporary { get; set; }
        public IDictionary<string, string> Properties { get; set; }

        public IDictionary<string, string> DocumentFilter { get; set; }

        public string GetProperty(string name)
        {
            return Properties[name];
        }

        public string GetProperty(string name, string defaultValue)
        {
            Properties.TryGetValue(name, out string val);
            if (val == null) val = defaultValue;
            return val;
        }

    }
}
