using System.Collections.Generic;
using SW.PrimitiveTypes;

namespace SW.Infolink.Domain
{
    public class Notifier:BaseEntity
    {

        public Notifier(string name,bool runOnSuccessfulResult, bool runOnBadResult, bool runOnFailedResult, string handlerId)
        {
            Name = name;
            RunOnSuccessfulResult = runOnSuccessfulResult;
            RunOnBadResult = runOnBadResult;
            RunOnFailedResult = runOnFailedResult;
            HandlerId = handlerId;
            Inactive = false;
        }

        public string Name { get; set; }
        public bool RunOnSuccessfulResult { get;  set; }
        public bool RunOnBadResult { get;  set; }
        public bool RunOnFailedResult { get; set; }
        public string HandlerId { get; set; }
        public bool Inactive { get; set; }
        public IReadOnlyDictionary<string, string> HandlerProperties { get; private set; }

        public void Update(bool runOnSuccessfulResult, bool runOnBadResult, bool runOnFailedResult, string handlerId,
            IReadOnlyDictionary<string, string> handlerProperties)
        {
            RunOnSuccessfulResult = runOnSuccessfulResult;
            RunOnBadResult = runOnBadResult;
            RunOnFailedResult = runOnFailedResult;
            HandlerId = handlerId;

            SetDictionaries(handlerProperties);
        }
        public void SetDictionaries(
            IReadOnlyDictionary<string, string> handler
        )
        {
            HandlerProperties = handler;
        }
        
        
        
        
    }
}