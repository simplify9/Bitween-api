using System.Collections.Generic;
using SW.PrimitiveTypes;

namespace SW.Bitween.Model
{
    public class NotifierCreate: IName
    {
        public string Name { get; set; }
    }

    public class NotifierUpdate:NotifierCreate
    {
        public bool RunOnSuccessfulResult { get;  set; }
        public bool RunOnBadResult { get;  set; }
        public bool RunOnFailedResult { get; set; }
        public string HandlerId { get; set; }
        public bool Inactive { get; set; }
        public ICollection<KeyAndValue> HandlerProperties { get; set; }
        public ICollection<NotifierSubscription> RunOnSubscriptions { get; set; }
    }

    public class NotifierSubscription
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class NotifierSearch
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool? RunOnSuccessfulResult { get;  set; }
        public bool? RunOnBadResult { get;  set; }
        public bool? RunOnFailedResult { get; set; }
        public string HandlerId { get; set; }
        public bool? Inactive { get; set; }
        /// <summary>So the list page can show a watched-integration count without a per-row detail fetch.</summary>
        public int[] RunOnSubscriptions { get; set; }
    }

   
}