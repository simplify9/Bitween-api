using System.Collections.Generic;
using SW.PrimitiveTypes;

namespace SW.Infolink.Model
{
    public class NotifierCreate
    {
        public string Name { get; set; }
        public bool? RunOnSuccessfulResult { get;  set; }
        public bool? RunOnBadResult { get;  set; }
        public bool? RunOnFailedResult { get; set; }
        public string HandlerId { get; set; }
    }

    public class NotifierUpdate
    {
        public string Name { get; set; }
        public bool? RunOnSuccessfulResult { get;  set; }
        public bool? RunOnBadResult { get;  set; }
        public bool? RunOnFailedResult { get; set; }
        public string HandlerId { get; set; }
        public bool Inactive { get; set; }
        public ICollection<KeyAndValue> HandlerProperties { get; set; }
    }

    public class NotifierSearch
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool? RunOnSuccessfulResult { get;  set; }
        public bool? RunOnBadResult { get;  set; }
        public bool? RunOnFailedResult { get; set; }
        public string HandlerId { get; set; }
    }

   
}