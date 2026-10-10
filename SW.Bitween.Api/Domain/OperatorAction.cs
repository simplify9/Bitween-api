using System;

namespace SW.Bitween.Domain;

/// <summary>
/// Something a member did that changes what Bitween does without changing its configuration:
/// retrying exchanges, exporting them, requeueing dead letters, deleting queues, running a scheduled
/// retry now. Recorded so the audit trail answers "who did that", as it does for configuration.
/// </summary>
public class OperatorAction
{
    public const string Retry = "retry";
    public const string BulkRetry = "bulk-retry";
    public const string Export = "export";
    public const string RunRetryNow = "run-retry-now";
    public const string RequeueDeadLetters = "requeue-dead-letters";
    public const string DeleteQueues = "delete-queues";

    private OperatorAction()
    {
    }

    public OperatorAction(string action, string target, string detail, string accountId)
    {
        Action = action;
        Target = Clip(target, 500);
        Detail = Clip(detail, 2000);
        AccountId = accountId;
        OccurredOn = DateTime.UtcNow;
    }

    public long Id { get; private set; }
    public string Action { get; private set; }

    /// <summary>What it was done to: an exchange id, a queue name, a selection.</summary>
    public string Target { get; private set; }

    /// <summary>How much it touched, in a sentence: "12 exchanges", "3 queues deleted, 1 skipped".</summary>
    public string Detail { get; private set; }

    public string AccountId { get; private set; }
    public DateTime OccurredOn { get; private set; }

    static string Clip(string value, int max) => value == null || value.Length <= max ? value : value[..max];
}
