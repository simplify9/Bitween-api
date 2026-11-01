using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services.Background;

/// <summary>
/// Where the admin UI hears that one of its background requests changed. It only listens: what it
/// does about it - collect the answer, cancel - goes through <c>/api/background</c>.
/// </summary>
/// <remarks>
/// Each connection is its signed-in user's, by the token's name identifier, which is what
/// <see cref="BackgroundRequests"/> sends to; a node knows only the connections it holds, so every
/// change is also broadcast for the others to pass on.
/// </remarks>
[Authorize]
public class BackgroundRequestHub : Hub
{
    public const string Path = "/api/background/hub";

    /// <summary>The message: <c>{ id, status }</c>.</summary>
    public const string Changed = "changed";
}

/// <summary>A background request changed, said to every node, for each to tell its own connections.</summary>
public class BackgroundRequestChanged
{
    public string Id { get; set; }
    public string Owner { get; set; }
    public BackgroundRequestStatus Status { get; set; }

    /// <summary>How far it got, for work that counts its steps.</summary>
    public int? Done { get; set; }

    public int? Total { get; set; }
    public string Summary { get; set; }

    /// <summary>The <see cref="BackgroundRequests.Node"/> that sent it, which told its own already.</summary>
    public string Node { get; set; }

    public static BackgroundRequestChanged Of(BackgroundRequest row, string node) => new()
    {
        Id = row.Id,
        Owner = row.Owner,
        Status = row.Status,
        Done = row.Done,
        Total = row.Total,
        Summary = row.Summary,
        Node = node,
    };
}

/// <summary>Its owner cancelled a background request, said to every node; the one running it stops it.</summary>
public class BackgroundRequestCancel
{
    public string Id { get; set; }
}

public class BackgroundRequestChangedListener(BackgroundRequests requests) : IListen<BackgroundRequestChanged>
{
    public Task Process(BackgroundRequestChanged message) =>
        message == null || message.Node == requests.Node
            ? Task.CompletedTask
            : requests.PushAsync(message);
}

public class BackgroundRequestCancelListener(BackgroundRequests requests) : IListen<BackgroundRequestCancel>
{
    public Task Process(BackgroundRequestCancel message)
    {
        if (message?.Id != null) requests.CancelHere(message.Id);
        return Task.CompletedTask;
    }
}
