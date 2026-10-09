using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Services.DataSources;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using SW.Bitween.Services.Adapters;
using SW.Serverless.Resident;

namespace SW.Bitween.Resources.DataSources;

/// <summary>
/// Reaches out to the broker and reports what happened, stage by stage.
///
/// This is the one control that turns configuring a data source from guesswork into something an
/// operator can finish: the alternative is to save it, wait up to thirty seconds for the supervisor
/// to reconcile, and then read the health column to learn that the password was wrong.
///
/// It runs the real adapter against the real settings, because a check that reimplements the
/// broker's handshake only proves that the reimplementation agrees with itself. What it does NOT do
/// is consume: the instance is started with Consume=false, so the customer's queue is inspected and
/// never drained, and it is stopped again before this returns.
/// </summary>
[HandlerName("test")]
public class Test(BitweenDbContext dbContext, RequestContext requestContext, BitweenOptions bitweenOptions,
    IResidentAdapterHost adapters = null) : ICommandHandler<int, DataSourceTestRequest, object>
{
    public async Task<object> Handle(int key, DataSourceTestRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.DataSources.Operate);

        // The host is registered on every node — Python and Node adapters run on it — but data
        // sources connect only where BusProvidersEnabled turns them on, so say which switch is off.
        // It is named for brokers because it predates database sources, so the message says what
        // it now gates rather than repeating a name that means nothing to someone configuring PostgreSQL.
        if (adapters == null || !bitweenOptions.BusProvidersEnabled)
            return Failed("Resident data source providers are turned off on this node, so nothing "
                          + "can connect from here. Turn on Bitween:BusProvidersEnabled — the "
                          + "switch is older than database sources and still carries the bus name.");

        var dataSource = await dbContext.Set<DataSource>().AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == key);

        if (dataSource == null)
            throw new SWNotFoundException($"DataSource with id '{key}' was not found");

        var startupValues = new Dictionary<string, string>(
            dataSource.Properties ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);

        // The endpoints its gateways want, so the test checks the queues that will actually be
        // used rather than only that the credentials work.
        var endpoints = await dbContext.Set<BusGateway>()
            .Where(g => g.DataSourceId == key && !g.Inactive && g.Endpoint != null)
            .Select(g => g.Endpoint)
            .Distinct()
            .ToListAsync();

        if (endpoints.Count > 0) startupValues["Endpoints"] = string.Join(",", endpoints);
        startupValues["Consume"] = "false";

        // Composed here as well as in the supervisor, because statements are rows rather than a
        // property on the data source — and without this the test quietly stopped covering them.
        // Preparing every statement against the live schema is most of what the button is FOR: it
        // is where a typo or a dropped column is caught, and a test that silently checks nothing
        // still reports success.
        var statements = await dbContext.Set<DataSourceStatement>()
            .Where(s => s.DataSourceId == key && !s.Inactive)
            .AsNoTracking()
            .ToListAsync();

        var composed = StatementComposer.Compose(statements, key);
        if (composed != null) startupValues["Statements"] = composed;

        // A key of its own: the running instance, if there is one, is keyed by the data source id
        // and must not be disturbed by someone pressing Test.
        var instanceKey = $"test-{key}-{Guid.NewGuid():N}"[..24];

        ResidentAdapterInstance instance = null;

        try
        {
            instance = await adapters.StartExclusiveAsync(new AdapterSpec
            {
                AdapterId = dataSource.AdapterId,
                InstanceKey = instanceKey,
                StartupValues = startupValues
            });

            var raw = await instance.InvokeAsync<JObject>("TestConnection", timeoutSeconds: 30);
            return Translate(raw);
        }
        catch (Exception ex)
        {
            // A broker that cannot be reached throws on start, before any stage runs. That is a
            // result, not a server error: the operator asked whether it works, and it does not.
            //
            // What Bitween observes in that case is only "Adapter stream closed" — true, and no
            // use at all. The adapter printed the actual reason before it died, so that is what
            // this answers with; without it, the one control meant to save reading a log is the
            // control that sends you to read one.
            return Failed(AdapterFailureReader.Explain(ex.Message,
                await AdapterFailureReader.SettledOutputAsync(instance)));
        }
        finally
        {
            try { await adapters.StopAsync(dataSource.AdapterId, instanceKey, drain: false); }
            catch { /* the instance may never have started */ }
        }
    }

    /// <summary>
    /// The adapters answer with { ok, steps: [{ step, ok, detail }] }, which is their shape rather
    /// than Bitween's. Translating here keeps the API contract stable while adapters evolve.
    /// </summary>
    private static DataSourceTestResult Translate(JObject raw)
    {
        if (raw == null) return Failed("The adapter returned nothing.");

        var result = new DataSourceTestResult { Succeeded = raw.Value<bool>("ok") };

        foreach (var step in raw["steps"] as JArray ?? [])
            result.Stages.Add(new DataSourceTestStage
            {
                Name = step.Value<string>("step"),
                Succeeded = step.Value<bool>("ok"),
                Detail = step.Value<string>("detail")
            });

        if (!result.Succeeded)
            result.Error = result.Stages.LastOrDefault(s => !s.Succeeded)?.Detail
                           ?? "The adapter reported a failure without naming a stage.";

        return result;
    }

    private static DataSourceTestResult Failed(string error) =>
        new() { Succeeded = false, Error = error };
}
