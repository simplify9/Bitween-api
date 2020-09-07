using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Infolink.Model
{

    public class SubscriptionCreate
    {
        public string Name { get; set; }
        public int DocumentId { get; set; }
        public SubscriptionType Type { get; set; }
        public int? PartnerId { get; set; }

    }

    public class SubscriptionSearch : SubscriptionUpdate
    {
        public int Id { get; set; }
        public string DocumentName { get; set; }
    }


    public class SubscriptionUpdate : SubscriptionCreate
    {
        public string HandlerId { get; set; }
        public string MapperId { get; set; }
        public string ReceiverId { get; set; }
        public bool Temporary { get; set; }
        public bool Aggregate { get; set; }
        public ICollection<KeyAndValue> HandlerProperties { get; set; }
        public ICollection<KeyAndValue> MapperProperties { get; set; }
        public ICollection<KeyAndValue> ReceiverProperties { get; set; }
        public ICollection<KeyAndValue> DocumentFilter { get; set; }
        public bool Inactive { get; set; }
        public ICollection<ScheduleView> Schedules { get; set; }
        public ICollection<ScheduleView> ReceiveSchedules { get; set; }
        public int? ResponseSubscriptionId { get; set; }
        public DateTime? ReceiveOn { get; set; }
        public int ReceiveConsecutiveFailures { get;  set; }
        public string ReceiveLastException { get;  set; }


    }
}
