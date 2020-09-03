using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Infolink.Model
{
    public class SubscriptionConfig
    {
        public string Name { get; set; }
        public int DocumentId { get; set; }
        public string HandlerId { get; set; }
        public string MapperId { get; set; }
        public bool Temporary { get; set; }
        public bool Aggregate { get; set; }
        public IDictionary<string, string> HandlerProperties { get; set; }
        public IDictionary<string, string> MapperProperties { get; set; }
        public IDictionary<string, string> DocumentFilter { get; set; }
        public bool Inactive { get; set; }
        public ICollection<ScheduleView> Schedules { get; set; }
        public int? ResponseSubscriptionId { get; set; }

    }
}
