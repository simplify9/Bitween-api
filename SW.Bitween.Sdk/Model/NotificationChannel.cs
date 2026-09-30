using System.Collections.Generic;
using SW.PrimitiveTypes;

namespace SW.Bitween.Model
{
    public class NotificationChannelCreate : IName
    {
        public string Name { get; set; } = null!;

        /// <summary>Any handler adapter, built in or custom.</summary>
        public string HandlerId { get; set; } = null!;

        /// <summary>Everything the handler needs to deliver: relay, login, recipients, url.</summary>
        public Dictionary<string, string> HandlerProperties { get; set; } = [];
    }

    public class NotificationChannelUpdate : NotificationChannelCreate { }

    public class NotificationChannelGet : NotificationChannelUpdate
    {
        public int Id { get; set; }
        public List<NotificationChannelUse> UsedBy { get; set; } = [];
    }

    public class NotificationChannelRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string HandlerId { get; set; } = null!;

        /// <summary>Every place that sends through this channel, so the list can name them.</summary>
        public List<NotificationChannelUse> UsedBy { get; set; } = [];
    }

    public enum NotificationChannelUseKind
    {
        /// <summary>A subscription's own notifications when its exchanges finish.</summary>
        Subscription,

        /// <summary>A retry policy's default budget alert.</summary>
        RetryPolicy,

        /// <summary>A group's budget alert, in a shared policy or in a subscription's inline one.</summary>
        RetryGroup,

        /// <summary>One subscription's alert for one group, overriding the policy.</summary>
        RetryAlertOverride
    }

    /// <summary>One place that sends through a channel.</summary>
    public class NotificationChannelUse
    {
        public NotificationChannelUseKind Kind { get; set; }

        /// <summary>Set when the use lives on a subscription, inline retry groups included.</summary>
        public int? SubscriptionId { get; set; }

        /// <summary>Set when the use lives on a shared retry policy.</summary>
        public int? RetryPolicyId { get; set; }

        /// <summary>Reads on its own, e.g. "Orders to SAP" or "Default policy › Timeouts".</summary>
        public string Name { get; set; } = null!;
    }

    /// <summary>
    /// When a subscription's finished exchanges are reported, and where to. A subscription can hold
    /// several, so failures and successes can go to different people.
    /// </summary>
    public class SubscriptionNotification
    {
        public int ChannelId { get; set; }

        /// <summary>The exchange failed.</summary>
        public bool OnFailure { get; set; }

        /// <summary>The exchange finished, but its response was flagged bad.</summary>
        public bool OnBadResult { get; set; }

        /// <summary>The exchange finished with a good response.</summary>
        public bool OnSuccess { get; set; }
    }
}
