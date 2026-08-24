using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;

namespace SW.Bitween.Model
{
    public enum SubscriptionType
    {
        Unknown = 0,
        Internal = 1,
        ApiCall = 2,
        Receiving = 4,
        Aggregation = 8,
        GatewayApiCall = 16,
        BusGateway = 32,
    }

    public class SubscriptionReceiveNow
    {
    }

    public class SubscriptionPause
    {
    }

    public class SubscriptionAggregateNow
    {
    }

    public class SubscriptionSaveMapper
    {
        public string MapperId { get; set; }
        public ICollection<KeyAndValue> MapperProperties { get; set; }
    }

    public class SubscriptionTrailModel : TrailBaseModel
    {
        public int SubscriptionId { get; set; }
    }

    public class SearchSubscriptionTrailModel
    {
        public int? Limit { get; set; }
        public int? Offset { get; set; }
        public int SubscriptionId { get; set; }
    }

    // One execution of a scheduled subscription, out of the scheduler's own history.
    // Only Receiving and Aggregation subscriptions run on a schedule, so only they
    // have runs; everything else returns an empty list.
    public class SubscriptionRunModel
    {
        public DateTime StartedOn { get; set; }
        public DateTime? EndedOn { get; set; }
        public long? DurationMs { get; set; }

        /// <summary>Null while the run is still in progress.</summary>
        public bool? Success { get; set; }
        public string Error { get; set; }
        public string Node { get; set; }

        /// <summary>True when someone pressed Receive now / Aggregate now instead of waiting for the cron.</summary>
        public bool Manual { get; set; }
    }

    public class SubscriptionLastRunModel : SubscriptionRunModel
    {
        public int SubscriptionId { get; set; }

        /// <summary>Finished runs in the recent window — in-progress runs are excluded, being neither.</summary>
        public int RecentTotal { get; set; }

        /// <summary>How many of <see cref="RecentTotal"/> succeeded.</summary>
        public int RecentSucceeded { get; set; }
    }

    public class SearchSubscriptionRunsModel
    {
        public int? Limit { get; set; }
        public int SubscriptionId { get; set; }
    }

    public class SearchSubscriptionLastRunsModel
    {
    }

    /// <summary>
    /// One execution of a Receiving subscription's own receive step, recorded directly by
    /// <c>ReceivingJob</c> — independent of the scheduler's own run history, which only knows
    /// whether <c>Execute()</c> threw (it never does; the receive step's own failures are caught
    /// and reported here instead, alongside the successes and no-op checks history never covers).
    /// </summary>
    public enum ReceiveOutcome
    {
        Failed = 0,
        NoNewData = 1,
        Received = 2,
    }

    public class ReceiveAttemptExchangeRef
    {
        public string Id { get; set; }
        public bool? Status { get; set; }
        public bool? ResponseBad { get; set; }
        public IDictionary<string, string> PromotedProperties { get; set; }
    }

    public class ReceiveAttemptModel
    {
        public int Id { get; set; }
        public DateTime StartedOn { get; set; }
        public DateTime FinishedOn { get; set; }
        public ReceiveOutcome Outcome { get; set; }
        public string ErrorMessage { get; set; }
        public ICollection<ReceiveAttemptExchangeRef> Exchanges { get; set; }
    }

    public class SearchReceiveAttemptsModel
    {
        public int SubscriptionId { get; set; }
        public ReceiveOutcome? Outcome { get; set; }
        public int? Offset { get; set; }
        public int? Limit { get; set; }
    }

    /// <summary>
    /// Whether a scheduled subscription will actually fire — read from the scheduler
    /// itself, not from what Bitween thinks it configured. The two can disagree, and
    /// when they do it is silent: the UI shows a healthy job that never runs.
    /// </summary>
    public class SubscriptionScheduleHealthModel
    {
        public int SubscriptionId { get; set; }

        /// <summary>Schedules configured on the subscription.</summary>
        public int ScheduleCount { get; set; }

        /// <summary>Triggers the scheduler actually holds. Fewer than ScheduleCount means some schedule will never fire.</summary>
        public int TriggerCount { get; set; }

        /// <summary>Worst state across the subscription's triggers: Normal, Paused, Blocked, Error, Complete, or Missing.</summary>
        public string State { get; set; }

        /// <summary>The scheduler's own next fire time — computed from the cron, independently of Subscription.ReceiveOn.</summary>
        public DateTime? NextFireOn { get; set; }

        /// <summary>
        /// The subscription is flagged as running but the scheduler has nothing executing for it.
        /// Left behind when a run is killed rather than thrown: the flag is never cleared and
        /// every later fire is skipped by the concurrency guard, so the job silently stops.
        /// </summary>
        public bool Stuck { get; set; }
    }

    public class SearchSubscriptionScheduleHealthModel
    {
    }

    public abstract class SubscriptionCreateUpdateBase : IName
    {
        public string Name { get; set; }
        public int DocumentId { get; set; }
        public int? PartnerId { get; set; }
        public int? AggregationForId { get; set; }
    }

    /// <summary>
    /// How a subscription is configured — everything a person chooses. Shared by create and
    /// update so the two can't drift: a field here is applied by both, through the same code.
    /// <para>
    /// Runtime state (what the subscription has since done — failure counts, last exception,
    /// next fire time) deliberately lives on <see cref="SubscriptionUpdate"/> only. None of it
    /// is meaningful for something that doesn't exist yet.
    /// </para>
    /// </summary>
    public abstract class SubscriptionConfiguration : SubscriptionCreateUpdateBase
    {
        public string HandlerId { get; set; }
        public string MapperId { get; set; }
        public string ReceiverId { get; set; }
        public string ValidatorId { get; set; }
        public int? CategoryId { get; set; }
        public int? WorkGroupId { get; set; }

        public IPropertyMatchSpecification MatchExpression { get; set; }
        public ICollection<KeyAndValue> HandlerProperties { get; set; }
        public ICollection<KeyAndValue> ValidatorProperties { get; set; }
        public ICollection<KeyAndValue> MapperProperties { get; set; }
        public ICollection<KeyAndValue> ReceiverProperties { get; set; }
        public ICollection<KeyAndValue> DocumentFilter { get; set; }

        public ICollection<ScheduleView> Schedules { get; set; }
        public int? ResponseSubscriptionId { get; set; }
        public string ResponseMessageTypeName { get; set; }

        public int? RetryPolicyId { get; set; }
        public CustomRetryPolicy CustomRetryPolicy { get; set; }
    }

    /// <summary>
    /// Creates a subscription complete, in one transaction. Everything beyond
    /// <see cref="SubscriptionCreateUpdateBase"/> and <see cref="Type"/> is optional — a caller
    /// that sends only those still gets the empty, inactive subscription it always did.
    /// </summary>
    /// <summary>
    /// An integration defined while it is being wired up, so the integration and the thing
    /// that points at it land in one transaction instead of two calls that can half-succeed.
    /// <para>
    /// Deriving from <see cref="SubscriptionConfiguration"/> is the point: the whole pipeline
    /// is applied by the same code an ordinary create uses. The type is always the gateway's.
    /// <c>DocumentId</c> is ignored for a bus gateway, which is bound to one information type and
    /// imposes it; an API gateway is not bound to one, so there it is required.
    /// </para>
    /// </summary>
    public class InlineIntegrationCreate : SubscriptionConfiguration
    {
    }

    public class SubscriptionCreate : SubscriptionConfiguration
    {
        public SubscriptionType Type { get; set; }

        /// <summary>
        /// Null (the default) means born inactive, as subscriptions always have been. Pass
        /// <c>false</c> to have it live the moment it exists. Nullable on purpose: a plain bool
        /// would silently activate every caller that doesn't mention it.
        /// </summary>
        public bool? Inactive { get; set; }
    }

    public class SubscriptionSearch : SubscriptionGet
    {
        public int Id { get; set; }
        public string DocumentName { get; set; }
        public bool? IsRunning { get; set; }
    }

    public class SubscriptionUpdate : SubscriptionConfiguration
    {
        public bool Temporary { get; set; }
        public bool Inactive { get; set; }

        public DateTime? ReceiveOn { get; set; }
        public DateTime? AggregateOn { get; set; }
        public int ConsecutiveFailures { get; set; }
        public string LastException { get; set; }
        public XchangeFileType AggregationTarget { get; set; }
        public DateTime? PausedOn { get; set; }
        public string CategoryCode { get; set; }
        public string CategoryDescription { get; set; }
    }

    public class SubscriptionGet : SubscriptionUpdate
    {
        public SubscriptionType Type { get; set; }
    }
}