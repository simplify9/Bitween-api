using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Services.Metrics;
using SW.PrimitiveTypes;
using SW.Serverless.Contract.Catalog;

namespace SW.Bitween.Services.Adapters;

/// <summary>
/// Picks the runtime an adapter belongs to and opens a session against it.
///
/// The pipeline asks for a mapper, handler, validator or receiver by id and role; which of the
/// three runtimes ends up running it is decided here and nowhere else. That is what keeps the
/// choice from being copied into every call site - and what lets a test replace the whole thing.
///
/// Every adapter call passes through here, so this is also where each one is counted for the
/// adapter metrics: how long it took, and whether it failed and how - a timeout, a stop for going
/// over a limit, a process that died, or an error of the adapter's own.
/// </summary>
public class AdapterInvoker(IEnumerable<IAdapterRuntime> runtimes, MetricsRecorder metrics = null, AdapterCatalog catalog = null,
    Connections.ConnectionTokens connections = null, Marketplace.RegistryLicences licences = null)
    : IAdapterInvoker
{
    // Order matters: the classic runtime claims everything, so it has to be asked last.
    private readonly IReadOnlyList<IAdapterRuntime> _runtimes = runtimes.ToList();

    public async Task<IAdapterSession> BeginAsync(string adapterId, AdapterRole role,
        IDictionary<string, string> properties = null, string correlationId = null)
    {
        // Connection references become tokens here, as the adapter starts, and nowhere earlier: an
        // exchange stores its settings with the reference in them, so no token is ever saved on
        // one, and a retry later still gets a token that works. Every role and runtime passes
        // through this, so every adapter can use them.
        if (connections != null) properties = await connections.ResolveAsync(properties);

        // A paid connector from the registry runs only on a licence that is live or in grace. Read
        // from the catalog's and the licence store's caches, so nothing is fetched on a call.
        if (licences != null) await licences.EnsureLicensedAsync(adapterId);

        foreach (var runtime in _runtimes)
            if (await runtime.CanRunAsync(adapterId))
            {
                if (metrics == null) return await runtime.BeginAsync(adapterId, role, properties, correlationId);

                var meter = new Meter(metrics, adapterId, await VersionOf(adapterId), role, RuntimeKind(runtime));
                var clock = Stopwatch.StartNew();
                try
                {
                    return new MeteredSession(await runtime.BeginAsync(adapterId, role, properties, correlationId), meter);
                }
                catch (Exception ex)
                {
                    // A session that couldn't even start is a failed call: a process that wouldn't spawn.
                    meter.Record(clock.ElapsedMilliseconds, ex);
                    throw;
                }
            }

        throw new BitweenException($"No runtime here knows how to run adapter '{adapterId}'.");
    }

    public async Task<TResult> InvokeAsync<TResult>(string adapterId, AdapterRole role, string method,
        object argument, IDictionary<string, string> properties = null, string correlationId = null)
    {
        await using var session = await BeginAsync(adapterId, role, properties, correlationId);
        return await session.InvokeAsync<TResult>(method, argument);
    }

    /// <summary>
    /// The version that runs: the pinned one, or for an unpinned reference the catalog's current
    /// version now (read from the catalog's one-minute cache, which the runtimes have just read).
    /// </summary>
    async Task<string> VersionOf(string adapterRef)
    {
        if (string.IsNullOrWhiteSpace(adapterRef) || NativeAdapterDiscoveryService.IsNative(adapterRef)) return "";
        var (id, version) = AdapterCatalogPaths.Split(adapterRef);
        if (version != null) return version;
        try
        {
            return catalog == null ? "" : (await catalog.GetAsync(id))?.Current ?? "";
        }
        catch
        {
            return "";
        }
    }

    static string RuntimeKind(IAdapterRuntime runtime) => runtime switch
    {
        NativeAdapterRuntime => "native",
        ResidentAdapterRuntime => "resident",
        ClassicAdapterRuntime => "process",
        _ => runtime.GetType().Name.ToLowerInvariant(),
    };

    /// <summary>How a failed call failed, from what the runtimes throw.</summary>
    public static AdapterFailure Classify(Exception ex) => ex switch
    {
        null => AdapterFailure.None,
        TimeoutException => AdapterFailure.Timeout,
        SW.Serverless.Resident.AdapterStoppedException { LimitExceeded: true } => AdapterFailure.LimitStop,
        SW.Serverless.Resident.AdapterStoppedException => AdapterFailure.Crash,
        IOException io when io.Message.Contains("stream closed", StringComparison.OrdinalIgnoreCase) => AdapterFailure.Crash,
        _ when ex.Message.Contains("Process not started or terminated", StringComparison.OrdinalIgnoreCase) => AdapterFailure.Crash,
        _ => AdapterFailure.Error,
    };

    sealed class Meter(MetricsRecorder metrics, string adapterRef, string version, AdapterRole role, string runtime)
    {
        readonly string _adapterId = AdapterCatalogPaths.Split(adapterRef).Item1;

        public void Record(long ms, Exception ex) =>
            metrics.AdapterCall(_adapterId, version, role.ToString().ToLowerInvariant(), runtime, ms, Classify(ex), ex?.Message);
    }

    /// <summary>A session whose every call is timed and counted.</summary>
    sealed class MeteredSession(IAdapterSession inner, Meter meter) : IAdapterSession
    {
        public async Task<TResult> InvokeAsync<TResult>(string method, object argument = null)
        {
            var clock = Stopwatch.StartNew();
            try
            {
                var result = await inner.InvokeAsync<TResult>(method, argument);
                meter.Record(clock.ElapsedMilliseconds, null);
                return result;
            }
            catch (Exception ex)
            {
                meter.Record(clock.ElapsedMilliseconds, ex);
                throw;
            }
        }

        public async Task InvokeAsync(string method, object argument = null)
        {
            var clock = Stopwatch.StartNew();
            try
            {
                await inner.InvokeAsync(method, argument);
                meter.Record(clock.ElapsedMilliseconds, null);
            }
            catch (Exception ex)
            {
                meter.Record(clock.ElapsedMilliseconds, ex);
                throw;
            }
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
