using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System;
using System.Linq;

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

        /// <summary>
        /// Which node processed it, which versions of the mapper and handler ran (the pinned one, or
        /// the current one at the time), and how long it took from arriving to finishing. Null on
        /// results from before these were recorded.
        /// </summary>
        public string NodeName { get; private set; }
        public string MapperVersion { get; private set; }
        public string HandlerVersion { get; private set; }
        public int? DurationMs { get; private set; }

        public void Stamp(string nodeName, string mapperVersion, string handlerVersion, long durationMs)
        {
            NodeName = Cut(nodeName, 100);
            MapperVersion = Cut(mapperVersion, 50);
            HandlerVersion = Cut(handlerVersion, 50);
            DurationMs = (int)System.Math.Clamp(durationMs, 0, int.MaxValue);
        }

        static string Cut(string value, int length) =>
            string.IsNullOrEmpty(value) ? null : value.Length <= length ? value : value[..length];

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

        /// <summary>The longest <see cref="Warning"/> kept.</summary>
        public const int WarningMaxLength = 2000;

        /// <summary>
        /// Something about a successful result its reader should know, or <c>null</c> for nothing:
        /// a delivery that went through while a file of it could not be stored, one line per file.
        /// Null on results from before it was recorded.
        /// </summary>
        public string Warning { get; private set; }

        /// <summary>
        /// Records that the delivery went through but the output or response file could not be
        /// stored: the file is recorded as missing (no size, name or hash, so nothing links to it)
        /// and the warning says why. Its content type and bad flag stay, since they describe what
        /// the handler did rather than the file.
        /// </summary>
        public void FileNotStored(XchangeFileType type, string reason)
        {
            if (type == XchangeFileType.Output)
            {
                OutputSize = 0;
                OutputName = null;
                OutputHash = null;
            }
            else if (type == XchangeFileType.Response)
            {
                ResponseSize = 0;
                ResponseName = null;
                ResponseHash = null;
            }
            else throw new ArgumentOutOfRangeException(nameof(type), type, "Only the output and response files are stored after delivery.");

            // Each line gets its share of the column, so a long first reason never cuts the second off.
            var line = NotStoredPrefix(type) + Cut(reason ?? "unknown error", WarningMaxLength / 2 - 60);
            Warning = Warning == null ? line : Warning + "\n" + line;
        }

        /// <summary>
        /// Why <paramref name="type"/>'s file could not be stored after the delivery, read back from
        /// <see cref="Warning"/>; <c>null</c> when it was stored, or never written.
        /// </summary>
        public string NotStoredReason(XchangeFileType type) => NotStoredReason(Warning, type);

        /// <inheritdoc cref="NotStoredReason(XchangeFileType)"/>
        public static string NotStoredReason(string warning, XchangeFileType type)
        {
            if (warning == null) return null;
            var prefix = NotStoredPrefix(type);
            return warning.Split('\n').FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
        }

        static string NotStoredPrefix(XchangeFileType type) =>
            $"Delivered; the {type.ToString().ToLowerInvariant()} file could not be stored: ";

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
        /// The subscription the exchange ran for, copied here so a subscription's latest result is
        /// one indexed read. Null on results from before it was recorded, and for routing exchanges.
        /// </summary>
        public int? SubscriptionId { get; private set; }

        /// <summary>
        /// How many failed deliveries in a row this result makes for its subscription: 0 for a
        /// delivery that went through (a bad response included), the previous result's count plus
        /// one for a failure. Counted for subscriptions set to pause after failures; null otherwise,
        /// and on results from before it was recorded. What auto-pause counts, instead of a counter
        /// on the subscription row that every exchange had to update.
        /// </summary>
        public int? ConsecutiveFailures { get; private set; }

        public void SetSubscription(int? subscriptionId) => SubscriptionId = subscriptionId;

        public void SetConsecutiveFailures(int? count) => ConsecutiveFailures = count;

        /// <summary>
        /// Drops the result event when nothing is set to hear about it: no notifier and no
        /// notification of the subscription's covers this outcome. Saves the result lane a message,
        /// and the two reads it made only to find that out. Any other event raised on the result,
        /// such as a spent retry budget, still goes.
        /// </summary>
        public void NothingToNotify()
        {
            foreach (var created in Events.OfType<XchangeResultCreatedEvent>().ToList())
                Events.Remove(created);
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
