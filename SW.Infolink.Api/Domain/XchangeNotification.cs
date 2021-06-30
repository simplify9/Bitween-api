using System;
using SW.PrimitiveTypes;

namespace SW.Infolink.Domain
{
    public class XchangeNotification:BaseEntity
    {
        private XchangeNotification(){}

        public XchangeNotification(string xchangeId, int notifierId, string notifierName, string exception = null)
        {
            XchangeId = xchangeId;
            FinishedOn = DateTime.UtcNow;
            Success = exception == null;
            Exception = exception;
            NotifierId = notifierId;
            NotifierName = NotifierName;
        }
        

        public string XchangeId { get; private set; }
        public bool Success { get; set; }

        public int NotifierId { get; set; }
        public string NotifierName { get; set; }
        
        
        public string Exception { get; private set; }
        public DateTime FinishedOn { get; private set; }
    }
}