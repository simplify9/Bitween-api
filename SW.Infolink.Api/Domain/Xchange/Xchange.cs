using SW.PrimitiveTypes;
using System;

namespace SW.Infolink.Domain
{
    public class Xchange : BaseEntity
    {
        private Xchange()
        {
        }

        public Xchange(int subscriberId, int documentId, XchangeFile file, string[] references = null)        {
        
            SubscriberId = subscriberId;
            DocumentId = documentId;
            References = references;
            InputFileName = file.Filename;
            InputFileSize = file.Data.Length;
            InputFileHash = file.Hash;
            StartedOn = DateTime.UtcNow;
            Status = XchangeStatus.Running;

        }

        public void SetSubmitted(Subscriber subscriber)
        {
            Events.Add(new XchangeSubmittedEvent
            {
                Xchange = this,
                Subscriber = subscriber
            });
        }

        public void SetScheduled(DateTime deliverOn)
        {
            Status = XchangeStatus.Success;
            DeliverOn = deliverOn;
            FinishedOn = DateTime.UtcNow;
        }

        public void SetSuccess()
        {
            Status = XchangeStatus.Success;
            FinishedOn = DateTime.UtcNow;
        }

        public void SetFailure(Exception exception)
        {
            Exception = exception.ToString();
            Status = XchangeStatus.Failure;
            FinishedOn = DateTime.UtcNow;
        }
        public void SetFailure(string exceptionString)
        {
            Exception = exceptionString;
            Status = XchangeStatus.Failure;
            FinishedOn = DateTime.UtcNow;
        }

        public int SubscriberId { get; private set; }
        public int DocumentId { get; private set; }
        public string HandlerId { get; set; }
        public string MapperId { get; set; }
        public string[] References { get; private set; }
        public XchangeStatus Status { get;private set; }
        public string Exception { get;private set; }
        public DateTime? DeliveredOn { get; set; }
        public DateTime? FinishedOn { get; private set; }
        public DateTime StartedOn { get; private set; }
        public string InputFileName { get; private set; }
        public int InputFileSize { get; private set; }
        public string InputFileHash { get; private set; }
        public DateTime? DeliverOn { get; private set; }
        public int ResponseXchangeId { get; set; }

    }
}
