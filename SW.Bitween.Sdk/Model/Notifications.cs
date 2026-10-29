using System;

namespace SW.Bitween.Model
{
    public class NotificationsSearch
    {
        public int Id { get; set; }
        public string XchangeId { get; set; } = null!;
        public string NotifierName { get; set; } = null!;
        public bool Success { get; set; }

        /// <summary>Held back by the quiet window: not sent, and counted in the next message instead.</summary>
        public bool Suppressed { get; set; }

        /// <summary>Null when the notification succeeded.</summary>
        public string? Exception { get; set; }
        public DateTime FinishedOn { get; set; }

        /// <summary>The channel it went through. Null for a legacy notifier's send.</summary>
        public int? ChannelId { get; set; }

        /// <summary>The subscription whose exchange it was about. Null once the exchange is gone.</summary>
        public int? SubscriptionId { get; set; }
        public string? SubscriptionName { get; set; }
    }
}