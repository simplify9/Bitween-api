using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;

namespace SW.Infolink.Model
{

    //public enum XchangeStatus
    //{
    //    Running,
    //    Failed,
    //    Succeded
    //}

    public enum XchangeFileType
    {
        Input,
        Output,
        Response
    }

    public class CreateXchange
    {
        public CreateXchangeOption Option { get; set; }
        public int? DocumentId { get; set; }
        public int? SubscriberId { get; set; }
        public string Data { get; set; }
    }
    
    public enum CreateXchangeOption
    {
        DocumentId,
        SubscriberId
    }
   

    public class XchangeRetry
    {
        public string Reason { get; set; }
        public bool Reset { get; set; }
    }

    public class XchangeGetResultResponse
    {
        public bool Success { get; set; }
        public string InputUri { get; set; }
        public string OutputUri { get; set; }
        public string ResponseUri { get; set; }
    }

    //public class XchangeUnderProcessing : IUnderProcessing
    //{
    //    public string Uri { get; set; }
    //}

    public class XchangeRow
    {
        public string Id { get; set; }
        public int? SubscriptionId { get; set; }
        public string SubscriptionName { get; set; }
        public int DocumentId { get; set; }
        public string DocumentName { get; set; }
        public string HandlerId { get; set; }
        public string MapperId { get; set; }
        public IEnumerable<string> References { get; set; }
        public bool? Status { get; set; }
        public int StatusFilter { get; set; }
        public string StatusString { get; set; }
        public string Exception { get; set; }
        public DateTime? DeliveredOn { get; set; }
        public DateTime? FinishedOn { get; set; }
        public DateTime? AggregatedOn { get; set; }
        public DateTime StartedOn { get; set; }
        public string InputFileName { get; set; }
        public int InputFileSize { get; set; }
        public string InputFileHash { get; set; }
        public string InputUrl { get; set; }
        public string OutputUrl { get; set; }
        public string ResponseUrl { get; set; }
        public string Duration { get; set; }
        public IDictionary<string, string> PromotedProperties { get; set; }
        public string PromotedPropertiesRaw { get; set; }
        public string RetryFor { get;  set; }
        public string AggregationXchangeId { get;  set; }
        public bool  OutputBad { get; set; }
        public bool ResponseBad { get; set; }
    }
}