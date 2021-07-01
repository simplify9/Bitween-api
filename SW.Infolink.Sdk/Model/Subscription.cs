using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;

namespace SW.Infolink.Model
{
    public enum SubscriptionType
    {
        Unknown = 0,
        Internal = 1,
        ApiCall = 2,
        Receiving = 4,
        Aggregation = 8
    }

    public class SubscriptionReceiveNow
    { 
    
    }
    
    public class SubscriptionPause
    {
        
    }

    public class SubscriptionAggregateNow
    {

    }

    public class SubscriptionCreate : IName
    {
        public string Name { get; set; }
        public int DocumentId { get; set; }
        public SubscriptionType Type { get; set; }
        public int? PartnerId { get; set; }
        public int? AggregationForId { get; set; }
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
        public string ValidatorId { get; set; }
        public bool Temporary { get; set; }
        public ICollection<KeyAndValue> HandlerProperties { get; set; }
        public ICollection<KeyAndValue> ValidatorProperties { get; set; }
        public ICollection<KeyAndValue> MapperProperties { get; set; }
        public ICollection<KeyAndValue> ReceiverProperties { get; set; }
        public ICollection<KeyAndValue> DocumentFilter { get; set; }
        public bool Inactive { get; set; }
        //public ICollection<ScheduleView> AggregationSchedules { get; set; }
        public ICollection<ScheduleView> Schedules { get; set; }
        public int? ResponseSubscriptionId { get; set; }
        public string ResponseMessageTypeName { get;  set; }

        public DateTime? ReceiveOn { get; set; }
        public DateTime? AggregateOn { get; set; }
        public int ConsecutiveFailures { get;  set; }
        public string LastException { get;  set; }
        public XchangeFileType AggregationTarget { get; set; }
        public DateTime? PausedOn { get; set; }
    }
}
