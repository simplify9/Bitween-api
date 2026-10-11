using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SW.PrimitiveTypes;

namespace SW.Bitween.Services.Adapters;

/// <summary>
/// Picks the runtime an adapter belongs to and opens a session against it.
///
/// The pipeline asks for a mapper, handler, validator or receiver by id and role; which of the
/// three runtimes ends up running it is decided here and nowhere else. That is what keeps the
/// choice from being copied into every call site - and what lets a test replace the whole thing.
/// </summary>
public class AdapterInvoker(IEnumerable<IAdapterRuntime> runtimes) : IAdapterInvoker
{
    // Order matters: the classic runtime claims everything, so it has to be asked last.
    private readonly IReadOnlyList<IAdapterRuntime> _runtimes = runtimes.ToList();

    public async Task<IAdapterSession> BeginAsync(string adapterId, AdapterRole role,
        IDictionary<string, string> properties = null, string correlationId = null)
    {
        foreach (var runtime in _runtimes)
            if (await runtime.CanRunAsync(adapterId))
                return await runtime.BeginAsync(adapterId, role, properties, correlationId);

        throw new BitweenException($"No runtime here knows how to run adapter '{adapterId}'.");
    }

    public async Task<TResult> InvokeAsync<TResult>(string adapterId, AdapterRole role, string method,
        object argument, IDictionary<string, string> properties = null, string correlationId = null)
    {
        await using var session = await BeginAsync(adapterId, role, properties, correlationId);
        return await session.InvokeAsync<TResult>(method, argument);
    }
}
