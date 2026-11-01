using System;

namespace SW.Bitween.Domain;

/// <summary>
/// Work the admin UI asked for that took longer than it would wait for - prod's list to bring from,
/// a bulk retry, a zip of exchange files - carried on without it. The answer is kept until the UI
/// collects it, so any node can hand it over, whichever ran it.
/// </summary>
/// <remarks>
/// Only what the UI asks to carry on becomes one; see
/// <see cref="Services.Background.BackgroundRequestMiddleware"/>.
/// </remarks>
public class BackgroundRequest
{
    private BackgroundRequest()
    {
    }

    public BackgroundRequest(string id, string owner, string path, string label, string page, string node)
    {
        Id = id;
        Owner = owner;
        Path = path;
        Label = label;
        Page = page;
        Node = node;
        Status = BackgroundRequestStatus.Waiting;
        StartedOn = DateTime.UtcNow;
    }

    public string Id { get; private set; }

    /// <summary>The account that asked, by its name identifier. Nobody else sees it or its answer.</summary>
    public string Owner { get; private set; }

    /// <summary>What was asked: the method, path and query, as the API received them.</summary>
    public string Path { get; private set; }

    /// <summary>What the UI called it, for the header.</summary>
    public string Label { get; private set; }

    /// <summary>The UI address it was asked from, which is where its answer is shown.</summary>
    public string Page { get; private set; }

    /// <summary>The node running it, as its heartbeat names it.</summary>
    public string Node { get; private set; }

    public BackgroundRequestStatus Status { get; private set; }
    public DateTime StartedOn { get; private set; }
    public DateTime? FinishedOn { get; private set; }

    /// <summary>How far it has got, for work that says: done of total, and in words.</summary>
    public int? Done { get; private set; }

    public int? Total { get; private set; }
    public string Summary { get; private set; }

    /// <summary>The answer's HTTP status; null until there is one.</summary>
    public int? ResultStatus { get; private set; }

    public string ResultContentType { get; private set; }

    /// <summary>The answer's Content-Disposition, for one that is a file to save.</summary>
    public string ResultDisposition { get; private set; }

    /// <summary>A small answer, kept here.</summary>
    public byte[] Result { get; private set; }

    /// <summary>A large one - a zip - kept in storage under this key instead.</summary>
    public string ResultKey { get; private set; }

    public bool Finished => Status is not (BackgroundRequestStatus.Waiting or BackgroundRequestStatus.Running);

    /// <summary>Whether there is an answer to hand over, which work cancelled part way can have too.</summary>
    public bool Answered => ResultStatus != null;

    public void Run() => Status = BackgroundRequestStatus.Running;

    public void Progress(int done, int total, string summary)
    {
        Done = done;
        Total = total;
        Summary = summary;
    }

    /// <summary>
    /// What the request answered, whatever it answered: an error is an answer too. Work its owner
    /// cancelled, that stopped where it was and said how far it got, ends cancelled with its answer.
    /// </summary>
    public void Answer(int status, string contentType, string disposition, byte[] body, string key,
        bool cancelled = false)
    {
        ResultStatus = status;
        ResultContentType = contentType;
        ResultDisposition = disposition;
        Result = body;
        ResultKey = key;
        End(cancelled ? BackgroundRequestStatus.Cancelled : BackgroundRequestStatus.Done);
    }

    public void End(BackgroundRequestStatus status)
    {
        Status = status;
        FinishedOn = DateTime.UtcNow;
    }
}

public enum BackgroundRequestStatus
{
    /// <summary>Started, waiting for one of the node's places to run in.</summary>
    Waiting = 0,

    Running = 1,

    /// <summary>Answered; the answer is kept until it expires.</summary>
    Done = 2,

    /// <summary>Its owner cancelled it.</summary>
    Cancelled = 3,

    /// <summary>Ran past the longest a background request may go without progress.</summary>
    TimedOut = 4,

    /// <summary>The node running it stopped before it finished, as a deploy or restart does.</summary>
    Stopped = 5,
}
