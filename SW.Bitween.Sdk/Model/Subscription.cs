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

    public class SubscriptionCreate : SubscriptionCreateUpdateBase
    {
        public SubscriptionType Type { get; set; }
    }

    public class SubscriptionSearch : SubscriptionGet
    {
        public int Id { get; set; }
        public string DocumentName { get; set; }
        public bool? IsRunning { get; set; }
    }

    public class SubscriptionUpdate : SubscriptionCreateUpdateBase
    {
        public string HandlerId { get; set; }
        public string MapperId { get; set; }
        public string ReceiverId { get; set; }
        public string ValidatorId { get; set; }
        public int? CategoryId { get; set; }
        public int? WorkGroupId { get; set; }

        public bool Temporary { get; set; }
        public IPropertyMatchSpecification MatchExpression { get; set; }
        public ICollection<KeyAndValue> HandlerProperties { get; set; }
        public ICollection<KeyAndValue> ValidatorProperties { get; set; }
        public ICollection<KeyAndValue> MapperProperties { get; set; }
        public ICollection<KeyAndValue> ReceiverProperties { get; set; }
        public ICollection<KeyAndValue> DocumentFilter { get; set; }

        public bool Inactive { get; set; }

        //public ICollection<ScheduleView> AggregationSchedules { get; set; }
        public ICollection<ScheduleView> Schedules { get; set; }
        public int? ResponseSubscriptionId { get; set; }
        public string ResponseMessageTypeName { get; set; }

        public DateTime? ReceiveOn { get; set; }
        public DateTime? AggregateOn { get; set; }
        public int ConsecutiveFailures { get; set; }
        public string LastException { get; set; }
        public XchangeFileType AggregationTarget { get; set; }
        public DateTime? PausedOn { get; set; }
        public string CategoryCode { get; set; }
        public string CategoryDescription { get; set; }

        public int? RetryPolicyId { get; set; }
        public CustomRetryPolicy CustomRetryPolicy { get; set; }
    }

    public class SubscriptionGet : SubscriptionUpdate
    {
        public SubscriptionType Type { get; set; }
    }
}