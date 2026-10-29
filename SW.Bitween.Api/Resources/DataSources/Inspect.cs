using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using SW.Bitween.Domain.DataSources;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using SW.Serverless.Resident;

namespace SW.Bitween.Resources.DataSources;

/// <summary>
/// Asks the RUNNING adapter what it can see right now - the broker's topology, or the
/// database's catalog.
///
/// Deliberately not the same shape as <see cref="Test"/>. A test starts a throwaway instance,
/// because its whole job is to answer "would these settings work" before anything depends on them.
/// Discover and GetStats are questions about the connection that is actually serving traffic, so
/// they go to that instance - starting a second one would answer about a connection nobody is
/// using, and on a broker that authorises per-connection it might not even answer the same way.
///
/// Both are read-only: neither consumes, acknowledges or publishes anything.
/// </summary>
[HandlerName("inspect")]
public class Inspect(BitweenDbContext dbContext, RequestContext requestContext,
    IResidentAdapterHost adapters = null) : ICommandHandler<int, DataSourceInspectRequest, object>
{
    /// <summary>
    /// The commands this endpoint will relay. An allow-list rather than a passthrough: the
    /// adapter also exposes Publish, which writes to the customer's broker, and that is not
    /// something a View-level read should be able to reach by naming it in a request body.
    /// </summary>
    /// A database adapter widens this: Describe says what the engine and this login can do, and
    /// Discover walks the catalog. Both are read-only. Query, Execute, Call, Batch and BulkLoad are
    /// deliberately absent and must stay absent - a View-level read must not become a way to run
    /// SQL against a customer's database by naming it in a request body.
    private static readonly string[] Allowed = ["Discover", "GetStats", "Describe"];

    public async Task<object> Handle(int key, DataSourceInspectRequest request)
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.DataSources.View);

        var command = request?.Command ?? "Discover";
        if (!Allowed.Contains(command, StringComparer.OrdinalIgnoreCase))
            throw new SWException(
                $"'{command}' is not something this endpoint relays. Allowed: {string.Join(", ", Allowed)}.");

        var kind = await dbContext.Set<DataSource>().AsNoTracking()
            .Where(d => d.Id == key)
            .Select(d => (DataSourceKind?)d.Kind)
            .FirstOrDefaultAsync();

        if (kind == null)
            throw new SWNotFoundException($"DataSource with id '{key}' was not found");

        var instance = adapters?.Describe()
            .FirstOrDefault(h => h.InstanceKey == key.ToString());

        if (instance == null)
            return new DataSourceInspectResult
            {
                Ran = false,
                // Why "not here" happened depends on the kind, and the two answers call for
                // opposite reactions. An exclusive broker connection is held by one node, so this
                // is the normal answer everywhere else and nothing is wrong. A pooled database
                // connection is held by every node that runs work, so "not here" means it is not
                // running at all - which is a fault worth chasing.
                Error = kind == DataSourceKind.Relational
                    ? "This node is not running the adapter for this data source. A connection "
                    + "pool is held by every node, so this means it has not started - check the "
                    + "live connection panel for why."
                    : "This node is not running the adapter for this data source, so it has "
                    + "nothing to ask. A broker connection is exclusive: only the node holding "
                    + "it can answer.",
            };

        try
        {
            var live = adapters.Get(instance.AdapterId, instance.InstanceKey);
            if (live == null)
                return new DataSourceInspectResult { Ran = false, Error = "The adapter went away." };

            // Arguments reach the command as-is. They cannot widen what it does: the allow-list
            // above decides which commands exist here, and each of those only reads.
            var raw = await live.InvokeAsync<JObject>(command, request?.Arguments, timeoutSeconds: 30);
            return new DataSourceInspectResult
            {
                Ran = true,
                Command = command,
                Result = raw?.ToString(Newtonsoft.Json.Formatting.Indented) ?? "",
            };
        }
        catch (Exception ex)
        {
            // A command that fails is an answer about the broker, not a server error.
            return new DataSourceInspectResult { Ran = false, Command = command, Error = ex.Message };
        }
    }
}
