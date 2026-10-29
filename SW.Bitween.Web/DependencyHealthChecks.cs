using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SW.Bus.RabbitMqExtensions;

namespace SW.Bitween.Web
{
    /// <summary>
    /// What <c>/health/ready</c> asks: can this instance reach its database and its broker. Not
    /// asked by <c>/health</c> or <c>/health/live</c> - a liveness probe that failed on a database
    /// blip would restart every pod at once, which turns a short outage into a long one.
    /// </summary>
    internal static class DependencyHealthChecks
    {
        public const string ReadyTag = "ready";

        public static IHealthChecksBuilder AddDependencyChecks(this IHealthChecksBuilder builder) =>
            builder
                .AddCheck<DatabaseHealthCheck>("database", tags: [ReadyTag])
                .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: [ReadyTag]);
    }

    internal sealed class DatabaseHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The database is not reachable.");
        }
    }

    /// <summary>
    /// Asks the bus whether it is connected, rather than opening a connection of its own: the bus
    /// connects in the background after start and reconnects on its own after an outage, so a pod
    /// whose broker is down boots and is simply not ready until the bus has its connections and
    /// exchanges. Costs the broker nothing however tight the probe is.
    /// </summary>
    internal sealed class RabbitMqHealthCheck(IBusConnectionStatus bus) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(bus.IsConnected
                ? HealthCheckResult.Healthy()
                // Not LastError: its message can carry the broker address and user.
                : HealthCheckResult.Unhealthy("The bus is not connected to RabbitMQ."));
    }
}
