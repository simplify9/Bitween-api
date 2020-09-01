

using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SW.Infolink.Domain
{
    public class Subscriber : BaseEntity
    {
        public Subscriber(string name, int documentId)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            DocumentId = documentId;
            Schedules = new List<Schedule>();
            Properties = new Dictionary<string, string>();
            DocumentFilter = new Dictionary<string, string>();
        }

        public string Name { get; set; }
        //public int KeySetId { get; set; }
        public int DocumentId { get; set; }
        public string HandlerId { get; set; }
        public string MapperId { get; set; }
        public bool Temporary { get; set; }
        public bool Aggregate { get; set; }
        public IReadOnlyDictionary<string, string> Properties { get; set; }
        public IReadOnlyDictionary<string, string> DocumentFilter { get; set; }
        public bool Inactive { get; set; }
        public ICollection<Schedule> Schedules { get; private set; }
        public int ResponseSubscriberId { get; set; }

        public DateTime? NextSchedule()
        {
            DateTime? nextSchedule = null;
            foreach (var sched in Schedules)
            {
                var tmpNext = sched.Next();
                if (nextSchedule == null || tmpNext < nextSchedule)
                    nextSchedule = tmpNext;
            }
            return nextSchedule;
        }

        //public SubscriberDto ToSubscriberDto()
        //{
        //    return new SubscriberDto
        //    {
        //        Id = Id,
        //        Properties = Properties.ToDictionary()
        //    };
        //}
    }
}
