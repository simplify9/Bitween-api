using System.Collections.Generic;
using SW.PrimitiveTypes;

namespace SW.Infolink.Domain
{
    public class Notifier:BaseEntity
    {

        public Notifier(string name)
        {
            Name = name;
            Inactive = false;
        }

        public string Name { get; set; }
        public bool RunOnSuccessfulResult { get;  set; }
        public bool RunOnBadResult { get;  set; }
        public bool RunOnFailedResult { get; set; }
        public string HandlerId { get; set; }
        public bool Inactive { get; set; }
        public IReadOnlyDictionary<string, string> HandlerProperties { get; private set; }

        public int[] RunOnSubscriptions { get; set; }

        public void Update(string name, bool runOnSuccessfulResult, bool runOnBadResult, bool runOnFailedResult, string handlerId,bool inactive, int[] runOnSubscriptions)
        {
            Name = name;
            RunOnSuccessfulResult = runOnSuccessfulResult;
            RunOnBadResult = runOnBadResult;
            RunOnFailedResult = runOnFailedResult;
            HandlerId = handlerId;
            Inactive = inactive;
            RunOnSubscriptions = runOnSubscriptions;
        }
        public void SetDictionaries(
            IReadOnlyDictionary<string, string> handler
        )
        {
            HandlerProperties = handler;
        }
        
        
        
        
    }
}