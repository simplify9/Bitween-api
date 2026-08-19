using System;
using SW.PrimitiveTypes;

namespace SW.Bitween.Domain
{
    public class XchangeNotification:BaseEntity
    {
        /// <summary>Name recorded for rows written by the retry-budget alert rather than a notifier.</summary>
        public const string RetryBudgetAlertName = "Retry budget alert";

        private XchangeNotification(){}

        public XchangeNotification(string xchangeId, int? notifierId, string notifierName, string exception = null)
        {
            XchangeId = xchangeId;
            FinishedOn = DateTime.UtcNow;
            Success = exception == null;
            Exception = exception;
            NotifierId = notifierId;
            NotifierName = notifierName;
        }

        /// <summary>
        /// Logs an attempt to deliver a "retry budget exhausted" alert. These rows have no
        /// <see cref="NotifierId"/> because the alert is configured on the retry policy rather than
        /// on a notifier — which is also how the send is recognised as already done on a redelivery.
        /// </summary>
        public static XchangeNotification ForRetryBudgetAlert(string xchangeId, string exception = null) =>
            new(xchangeId, null, RetryBudgetAlertName, exception);


        public string XchangeId { get; private set; }
        public bool Success { get; set; }

        /// <summary>The notifier that produced this row, or <c>null</c> for a retry-budget alert.</summary>
        public int? NotifierId { get; set; }
        public string NotifierName { get; set; }
        
        
        public string Exception { get; private set; }
        public DateTime FinishedOn { get; private set; }
    }
}