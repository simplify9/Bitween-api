using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using SW.Serverless.Resident;

namespace SW.Bitween.Resources.DataSources;

/// <summary>
/// What this connection is doing right now.
///
/// The data source row carries a summary written back on the reconcile loop, which is up to thirty
/// seconds old and deliberately small. This reads the heartbeat directly, so an operator watching a
/// queue drain sees it drain — and gets the two things the row has never carried: the per-queue
/// depths the adapter can see, and the host-observed process figures that keep working when the
/// adapter is wedged and reporting nothing at all.
///
/// Scoped to THIS node, and says so. A broker connection is exclusive, so at most one node runs any
/// given adapter; every other node answers RunningHere=false rather than inventing an outage.
/// </summary>
[HandlerName("telemetry")]
public class Telemetry(BitweenDbContext dbContext, RequestContext requestContext,
    IResidentAdapterHost adapters = null) : IGetHandler<int, object>
{
    public async Task<object> Handle(int key)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.DataSources.View);

        var dataSource = await dbContext.Set<DataSource>().AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == key);

        if (dataSource == null)
            throw new SWNotFoundException($"DataSource with id '{key}' was not found");

        var telemetry = new DataSourceTelemetry
        {
            OwnedByNode = dataSource.OwnedByNode,

            // The row's own summary, so the answer is still useful when the adapter runs elsewhere
            // or has not started. Overwritten below by the live heartbeat when there is one.
            State = dataSource.LastKnownState,
            LastError = dataSource.LastException,
            RestartCount = dataSource.ConsecutiveFailures,
            LastHeartbeatOn = dataSource.LastHeartbeatOn,
        };

        // A node with BusProvidersEnabled off runs no data source, so it finds no instance and
        // answers "not here".
        var instance = adapters?.Describe()
            .FirstOrDefault(h => h.InstanceKey == key.ToString());

        if (instance == null) return telemetry;

        telemetry.RunningHere = true;
        telemetry.Connected = instance.Connected;
        telemetry.State = instance.ReportedState ?? instance.State.ToString();
        telemetry.LastMessageOn = instance.LastMessageOn?.UtcDateTime;
        telemetry.InFlight = instance.InFlight;
        telemetry.LastError = instance.LastError ?? dataSource.LastException;

        telemetry.ProcessId = instance.ProcessId;
        telemetry.WorkingSetBytes = instance.WorkingSetBytes;
        telemetry.CpuPercent = instance.CpuPercent;
        telemetry.ThreadCount = instance.ThreadCount;
        telemetry.Uptime = instance.Uptime;
        telemetry.RestartCount = instance.RestartCount;
        telemetry.MissedHeartbeats = instance.MissedHeartbeats;
        telemetry.Quarantined = instance.Quarantined;
        telemetry.LastHeartbeatOn = instance.LastHeartbeatOn?.UtcDateTime ?? dataSource.LastHeartbeatOn;

        foreach (var kv in instance.Details ?? new System.Collections.Generic.Dictionary<string, string>())
            telemetry.Details[kv.Key] = kv.Value;

        telemetry.Commands = (instance.Commands ?? Array.Empty<string>()).ToList();

        return telemetry;
    }
}
