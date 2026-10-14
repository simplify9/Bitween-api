using System.Threading.Tasks;
using SW.PrimitiveTypes;

namespace SW.Bitween;

/// <summary>
/// An exchange's result is saved, told to the node holding the synchronous call that waits for it.
/// What the call answers with travels in it, so the answer needs no read of its own.
/// </summary>
/// <remarks>
/// Broadcast to every node over SW-Bus's node exchange, as cache revokes are; only the node named in
/// <see cref="Node"/> acts on it. Sent only for a sync call's exchange whose worker ran on another
/// node; a worker on the same node rings the bell directly.
/// </remarks>
public class XchangeResultReady
{
    public string Id { get; set; }

    /// <summary>The <see cref="ResultSignals.Node"/> holding the call.</summary>
    public string Node { get; set; }

    public bool Success { get; set; }
    public bool ResponseBad { get; set; }
    public string OutputName { get; set; }
    public int OutputSize { get; set; }
    public int ResponseSize { get; set; }
    public string ResponseContentType { get; set; }

    /// <summary>What a saved result says, for the call waiting on it; null for none.</summary>
    public static XchangeResultReady From(Domain.XchangeResult result, string node = null) => result == null
        ? null
        : new XchangeResultReady
        {
            Id = result.Id,
            Node = node,
            Success = result.Success,
            ResponseBad = result.ResponseBad,
            OutputName = result.OutputName,
            OutputSize = result.OutputSize,
            ResponseSize = result.ResponseSize,
            ResponseContentType = result.ResponseContentType,
        };
}

/// <summary>Rings this node's bell for a result saved on another node; see <see cref="ResultSignals"/>.</summary>
public class ResultReadyListener(ResultSignals signals) : IListen<XchangeResultReady>
{
    public Task Process(XchangeResultReady message)
    {
        if (message?.Node == signals.Node) signals.Complete(message);
        return Task.CompletedTask;
    }
}
