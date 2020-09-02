using SW.PrimitiveTypes;
using System;
using System.Runtime.Serialization;

namespace SW.Infolink
{
    public class InfolinkException : SWException
    {
        public InfolinkException() {}
        public InfolinkException(string message) : base(message) {}
        public InfolinkException(string message, Exception innerException) : base(message, innerException) {}
    }

    public class DocumentSizeException : InfolinkException {}

    public class AdapterException : InfolinkException
    {
        public int ExitCode { get; }
        public AdapterException(int exitCode, string message) : base($"{exitCode}:{message}") => ExitCode = exitCode;
    }


    //public class MaximumDocumentSizeExceededException : InfolinkException
    //{
    //    public int MessageSize;
    //    public int MaximumMessageSize;
    //    public MaximumDocumentSizeExceededException(int MessageSize, int MaximumMessageSize) : base("Maximum document size exceeded, document size:" + MessageSize + ",documnet size limit: " + MaximumMessageSize)
    //    {
    //        this.MessageSize = MessageSize;
    //        this.MaximumMessageSize = MaximumMessageSize;
    //    }
    //}

    //public class InvalidEntityIdOrPinException : InfolinkException
    //{
    //    public InvalidEntityIdOrPinException() : base()
    //    {
    //    }
    //}

    public class DocumentHandlerNotFoundException : InfolinkException
    {
        public DocumentHandlerNotFoundException() : base()
        {
        }

    }


    public class UnSupportedDocumentDirectionException : InfolinkException
    {
        public int DocumentDirection;
        public UnSupportedDocumentDirectionException(int DocumentDirection)
        {
            this.DocumentDirection = DocumentDirection;
        }



        public UnSupportedDocumentDirectionException() : base()
        {
        }

    }


    public class SubscriberPropertyNotFoundException : InfolinkException
    {
        public int SubscriberID;
        public string PropertyName;

        public SubscriberPropertyNotFoundException(int SubscriberID, string PropertyName)
        {
            this.SubscriberID = SubscriberID;
            this.PropertyName = PropertyName;
        }

    }


    //public class DocumentMapException : InfolinkException
    //{
    //    public DocumentMapException(string Message) : base(Message)
    //    {
    //    }

    //}


    //public class AccessRequestParseError : InfolinkException
    //{
    //    public AccessRequestParseError(string Message) : base(Message)
    //    {
    //    }
    //}


    public class DuplicateDocumentFoundException : InfolinkException
    {
        public DuplicateDocumentFoundException(int DuplicateId) : base("Duplicate document transmission occurred, interchangelog ID:" + DuplicateId)
        {
        }

    }

    public class PromotedPropertyNotPresent : InfolinkException
    {
        public PromotedPropertyNotPresent(string Message) : base(Message)
        {
        }

    }
}
