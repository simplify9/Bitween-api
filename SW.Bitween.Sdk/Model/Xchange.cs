using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;

namespace SW.Bitween.Model
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

    /// <summary>
    /// Which exchanges a bulk action covers: a hand-picked list, or everything a search filter
    /// matches minus the rows unticked after selecting it.
    /// </summary>
    public interface IXchangeSelection
    {
        List<string>? Ids { get; }
        string? Filter { get; }
        List<string>? ExcludeIds { get; }
    }

    public class XchangeBulkRetry : XchangeRetry, IXchangeSelection
    {
        /// <summary>
        /// The exchanges the caller picked by hand. Ignored when <see cref="Filter"/> is set.
        /// </summary>
        public List<string> Ids { get; set; }

        /// <summary>
        /// A whole filter's worth of exchanges instead of a hand-picked list, as the same
        /// query-string fragment the exchange search takes (<c>filter=StatusFilter:1:3&amp;filter=PartnerId:1:7</c>).
        /// Sent when someone chose "select all matching", so the selection is not limited to
        /// the rows one page happened to show.
        /// </summary>
        public string Filter { get; set; }

        /// <summary>
        /// Exchanges to leave out of a <see cref="Filter"/> selection — the rows unticked after
        /// selecting everything.
        /// </summary>
        public List<string> ExcludeIds { get; set; }
    }

    /// <summary>
    /// The exchanges whose files go into one zip, chosen the same way as a bulk retry's. Nullable, unlike
    /// the bulk retry's: this one is bound by MVC, which would otherwise require all three.
    /// </summary>
    public class XchangeFilesExport : IXchangeSelection
    {
        /// <inheritdoc cref="XchangeBulkRetry.Ids"/>
        public List<string>? Ids { get; set; }

        /// <inheritdoc cref="XchangeBulkRetry.Filter"/>
        public string? Filter { get; set; }

        /// <inheritdoc cref="XchangeBulkRetry.ExcludeIds"/>
        public List<string>? ExcludeIds { get; set; }
    }

    /// <summary>
    /// What a bulk retry is about to do, so it can be shown before it is run. Returned by the
    /// preview and again by the retry itself, where the counts are what actually happened.
    /// </summary>
    public class XchangeBulkRetryPlan
    {
        /// <summary>How many exchanges the selection came to, before resolving any chains.</summary>
        public int Selected { get; set; }

        /// <summary>Distinct exchanges that will be (or were) retried.</summary>
        public int WillRetry { get; set; }

        /// <summary>The largest selection this endpoint will carry out in one request.</summary>
        public int Limit { get; set; }

        /// <summary>
        /// <c>true</c> when the selection is past <see cref="Limit"/>. Nothing else is filled in
        /// then — resolving thousands of chains to describe a request that will be refused
        /// costs more than the answer is worth.
        /// </summary>
        public bool OverLimit { get; set; }

        /// <summary>
        /// Selections that had already been retried, and the later attempt standing in for each.
        /// </summary>
        public List<XchangeRetrySubstitution> Substituted { get; set; } = new List<XchangeRetrySubstitution>();

        /// <summary>Selections nothing will be done about, each with the reason.</summary>
        public List<XchangeRetrySkip> Skipped { get; set; } = new List<XchangeRetrySkip>();

        /// <summary>
        /// Promoted properties for every exchange named in <see cref="Substituted"/> or
        /// <see cref="Skipped"/>, keyed by id, so the caller can name them the way the exchange
        /// list does. One lookup rather than a copy per entry, since one attempt is often named by
        /// several selections. Exchanges whose information type promotes nothing are absent.
        /// </summary>
        public Dictionary<string, IDictionary<string, string>> Properties { get; set; } =
            new Dictionary<string, IDictionary<string, string>>();
    }

    /// <summary>
    /// A selected exchange that had already been retried, paired with the newest attempt in its
    /// chain — the one a retry actually runs, since retrying the selected one again is refused.
    /// </summary>
    public class XchangeRetrySubstitution
    {
        public string SelectedId { get; set; }
        public string RetryId { get; set; }
    }

    public class XchangeRetrySkip
    {
        /// <summary>
        /// The exchange the skip is about: the one selected, or — once it had already been
        /// retried — the later attempt that stood in for it.
        /// </summary>
        public string Id { get; set; }

        public string Reason { get; set; }
    }

    public class XchangeRetryTreeRequest
    {
        /// <summary>Any exchange in the chain — the answer is the same whichever one is asked about.</summary>
        public string Id { get; set; }
    }

    /// <summary>
    /// Every attempt related to one exchange: the original, and each retry descended from it.
    /// </summary>
    public class XchangeRetryTree
    {
        /// <summary>The original attempt everything in <see cref="Nodes"/> descends from.</summary>
        public string RootId { get; set; }

        /// <summary>
        /// Flat, oldest first. Each node names its parent, so the caller rebuilds the shape
        /// without the server having to pick a rendering.
        /// </summary>
        public List<XchangeRetryNode> Nodes { get; set; } = new List<XchangeRetryNode>();

        /// <summary>
        /// <c>true</c> when the walk stopped at its depth limit, so the chain shown is only
        /// the part nearest the exchange asked about.
        /// </summary>
        public bool Truncated { get; set; }
    }

    public class XchangeRetryNode
    {
        public string Id { get; set; }
        public string RetryFor { get; set; }

        /// <summary>
        /// What the payload promoted, so an attempt can be named the way the exchange list names
        /// it. An id identifies an exchange but says nothing about which one it is.
        /// </summary>
        public IDictionary<string, string> PromotedProperties { get; set; }
        public DateTime StartedOn { get; set; }
        public DateTime? FinishedOn { get; set; }

        /// <summary><c>null</c> while the exchange is still running.</summary>
        public bool? Status { get; set; }

        public bool? ResponseBad { get; set; }
        public string Exception { get; set; }

        /// <summary><c>true</c> when a person asked for this attempt rather than a retry policy.</summary>
        public bool ManualRetry { get; set; }

        public DateTime? ScheduledRetryOn { get; set; }
        public string RetryBlockedReason { get; set; }
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
        public string[] References { get; set; }
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
        public string OutputFileName { get; set; }
        public int OutputFileSize { get; set; }
        public string ResponseFileName { get; set; }
        public int ResponseFileSize { get; set; }

        public string InputUrl { get; set; }
        public string OutputUrl { get; set; }
        public string ResponseUrl { get; set; }
        public string InputKey { get; set; }
        public string OutputKey { get; set; }
        public string ResponseKey { get; set; }
        public string Duration { get; set; }
        public IDictionary<string, string> PromotedProperties { get; set; }
        public string PromotedPropertiesRaw { get; set; }
        public string RetryFor { get; set; }
        public string AggregationXchangeId { get; set; }
        public bool? OutputBad { get; set; }
        public bool? ResponseBad { get; set; }
        public string CorrelationId { get; set; }
        public int? PartnerId { get; set; }
        public DateTime? ScheduledRetryOn { get; set; }

        /// <summary>Why the retry policy declined to schedule another attempt, when it declined.</summary>
        public string RetryBlockedReason { get; set; }

        /// <summary>
        /// <c>true</c> when something has already been retried from this exchange. An exchange
        /// gets at most one retry, so this is also what makes it un-retryable: the row's own
        /// Retry action gives way to a link to the later attempt.
        /// </summary>
        /// <remarks>
        /// Carried on the row rather than left to the retry-tree endpoint so that the common
        /// case — an exchange with no retries either side of it — needs no second request at
        /// all. Costs one index lookup per returned row, on the index over <c>RetryFor</c>.
        /// </remarks>
        public bool HasRetry { get; set; }
    }
}