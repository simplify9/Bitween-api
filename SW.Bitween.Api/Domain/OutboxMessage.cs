using System;
using System.Collections.Generic;

namespace SW.Bitween.Domain;

/// <summary>
/// A bus message waiting to be published, written in the same transaction as the change that
/// raised it.
/// </summary>
/// <remarks>
/// Messages used to be published straight after the commit. When the broker was unreachable at
/// that moment, the change was saved and its message was lost: an exchange that was stored and
/// never processed, with nothing to notice. Now the message is stored with the change, published
/// straight after the commit as before, and — when that fails — published later by
/// <c>OutboxDispatcher</c>. Delivery is at least once, so consumers must tolerate a repeat.
/// </remarks>
public class OutboxMessage
{
    private OutboxMessage()
    {
    }

    public OutboxMessage(string messageType, string body, Dictionary<string, string> headers = null)
    {
        MessageType = messageType;
        Body = body;
        Headers = headers;
        CreatedOn = DateTime.UtcNow;
    }

    public long Id { get; private set; }
    public string MessageType { get; private set; }
    public string Body { get; private set; }
    public Dictionary<string, string> Headers { get; private set; }
    public DateTime CreatedOn { get; private set; }

    /// <summary>Null until the broker has taken it.</summary>
    public DateTime? PublishedOn { get; private set; }

    /// <summary>A dispatcher's claim on it, so two replicas never publish the same row at once.</summary>
    public DateTime? ClaimedUntil { get; private set; }

    public int Attempts { get; private set; }
    public string LastError { get; private set; }
}
