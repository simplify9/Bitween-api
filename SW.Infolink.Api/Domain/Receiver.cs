

using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;

namespace SW.Infolink.Domain
{
    public class Receiver : BaseEntity
    {
        public Receiver(int id, string name, string receiverId)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Id = id;
            ReceiverId = receiverId;
            Schedules = new List<Schedule>();
            Properties = new Dictionary<string, string>();
        }

        public string Name { get; set; }
        public ICollection<Schedule> Schedules { get; private set; }
        public string ReceiverId { get; set; }
        public IReadOnlyDictionary<string, string> Properties { get; set; }
        public DateTime? ReceiveOn { get; set; }

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

    }
}
