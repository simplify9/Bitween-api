using SW.PrimitiveTypes;
using System;
using System.Runtime.Serialization;

namespace SW.Bitween
{
    public class BitweenException : SWException
    {
        public BitweenException() {}
        public BitweenException(string message) : base(message) {}
        public BitweenException(string message, Exception innerException) : base(message, innerException) {}
    }

    public class DocumentSizeException : BitweenException {}

    public class AdapterException : BitweenException
    {
        public int ExitCode { get; }
        public AdapterException(int exitCode, string message) : base($"{exitCode}:{message}") => ExitCode = exitCode;
    }


    //public class MaximumDocumentSizeExceededException : BitweenException
    //{
    //    public int MessageSize;
    //    public int MaximumMessageSize;
    //    public MaximumDocumentSizeExceededException(int MessageSize, int MaximumMessageSize) : base("Maximum document size exceeded, document size:" + MessageSize + ",documnet size limit: " + MaximumMessageSize)
    //    {
    //        this.MessageSize = MessageSize;
    //        this.MaximumMessageSize = MaximumMessageSize;
    //    }
    //}

    //public class InvalidEntityIdOrPinException : BitweenException
    //{
    //    public InvalidEntityIdOrPinException() : base()
    //    {
    //    }
    //}

    public class DocumentHandlerNotFoundException : BitweenException
    {
        public DocumentHandlerNotFoundException() : base()
        {
        }

    }


    public class UnSupportedDocumentDirectionException : BitweenException
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


    public class SubscriberPropertyNotFoundException : BitweenException
    {
        public int SubscriberID;
        public string PropertyName;

        public SubscriberPropertyNotFoundException(int SubscriberID, string PropertyName)
        {
            this.SubscriberID = SubscriberID;
            this.PropertyName = PropertyName;
        }

    }


    //public class DocumentMapException : BitweenException
    //{
    //    public DocumentMapException(string Message) : base(Message)
    //    {
    //    }

    //}


    //public class AccessRequestParseError : BitweenException
    //{
    //    public AccessRequestParseError(string Message) : base(Message)
    //    {
    //    }
    //}


    public class DuplicateDocumentFoundException : BitweenException
    {
        public DuplicateDocumentFoundException(int DuplicateId) : base("Duplicate document transmission occurred, interchangelog ID:" + DuplicateId)
        {
        }

    }

    public class PromotedPropertyNotPresent : BitweenException
    {
        public PromotedPropertyNotPresent(string Message) : base(Message)
        {
        }

    }
}
