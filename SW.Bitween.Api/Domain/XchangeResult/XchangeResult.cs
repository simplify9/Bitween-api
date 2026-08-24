using SW.PrimitiveTypes;
using System;

namespace SW.Bitween.Domain
{
    public class XchangeResult : BaseEntity<string>
    {
        private XchangeResult()
        {
        }

        public XchangeResult(string xchangeId,WorkGroup workGroup, XchangeFile outputFile, XchangeFile responseFile = null, string responseXchangeId = null, string exception = null)
        {
            Id = xchangeId;
            Success = exception == null;
            Exception = exception;
            FinishedOn = DateTime.UtcNow;
            ResponseXchangeId = responseXchangeId;

            if (outputFile != null)
            {
                OutputName = outputFile.Filename;
                OutputSize = outputFile.Data.Length;
                OutputHash = outputFile.Hash;
                OutputBad = outputFile.BadData;
                OutputContentType = outputFile.ContentType;
            }

            if (responseFile != null)
            {
                ResponseName = responseFile.Filename;
                ResponseSize = responseFile.Data.Length;
                ResponseHash = responseFile.Hash;
                ResponseBad = responseFile.BadData;
                ResponseContentType = responseFile.ContentType;

            }

            Events.Add(new XchangeResultCreatedEvent
            {
                Id = Id,
                Success = Success,
                ResponseBad = ResponseBad,
                WorkGroup =  workGroup ?? WorkGroup.None,
            });
        }

        public bool Success { get; private set; }
        public string Exception { get; private set; }
        public DateTime FinishedOn { get; private set; }
        public string ResponseXchangeId { get; private set; }

        public string OutputName { get; private set; }
        public int OutputSize { get; private set; }
        public string OutputHash { get; private set; }
        public bool OutputBad { get; private set; }
        public string OutputContentType { get; private set; }

        public string ResponseName { get; private set; }
        public int ResponseSize { get; private set; }
        public string ResponseHash { get; private set; }
        public bool ResponseBad { get; private set; }
        public string ResponseContentType { get; private set; }

        /// <summary>
        /// Why the retry policy declined to schedule another attempt for this failure, or
        /// <c>null</c> when a retry was scheduled or no policy applied. Without it a group that
        /// has exhausted its budget looks identical to one that never matched.
        /// </summary>
        public string RetryBlockedReason { get; private set; }

        /// <summary>Records the policy's refusal so it can be shown alongside the failure.</summary>
        public void SetRetryBlocked(string reason) => RetryBlockedReason = reason;

        /// <summary>
        /// The retry group that matched this failure, or <c>null</c> when no policy applied or none
        /// matched. The evaluator works this out and would otherwise discard it, leaving no way to
        /// ask which failures a group is responsible for.
        /// </summary>
        public Guid? RetryGroupId { get; private set; }

        /// <summary>
        /// How many times this message had already been attempted when the policy evaluated it
        /// (0 on the original run). Stored because deriving it means walking the whole
        /// <c>Xchange.RetryFor</c> chain one query at a time.
        /// </summary>
        public int? AttemptNumber { get; private set; }

        /// <summary>Records which group owned this failure, and how far into its retries it was.</summary>
        public void SetRetryEvaluation(Guid groupId, int attemptNumber)
        {
            RetryGroupId = groupId;
            AttemptNumber = attemptNumber;
        }

        /// <summary>
        /// Announces that this failure was the one that emptied the group's shared budget. Only ever
        /// called by the caller that won the claim, so the event is raised once per exhaustion.
        /// </summary>
        public void RaiseBudgetExhausted(int subscriptionId, Guid groupId, string groupName,
            int maxAttemptsTotal)
        {
            Events.Add(new RetryBudgetExhaustedEvent
            {
                XchangeId = Id,
                SubscriptionId = subscriptionId,
                GroupId = groupId,
                GroupName = groupName,
                MaxAttemptsTotal = maxAttemptsTotal,
                OccurredOn = DateTime.UtcNow
            });
        }
    }
}
