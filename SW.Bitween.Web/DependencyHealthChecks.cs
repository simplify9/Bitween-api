using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace SW.Bitween.Web
{
    /// <summary>
    /// What <c>/health/ready</c> asks: can this instance reach its database and its broker. Not
    /// asked by <c>/health</c> or <c>/health/live</c> — a liveness probe that failed on a database
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
    /// Opens and closes a connection, at most every ten seconds whatever the probe interval, so a
    /// tight readiness probe on many replicas doesn't churn connections on the broker.
    /// </summary>
    internal sealed class RabbitMqHealthCheck(IConfiguration configuration) : IHealthCheck
    {
        private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static (DateTime At, HealthCheckResult Result)? _last;

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            if (_last is { } cached && DateTime.UtcNow - cached.At < CacheFor) return cached.Result;

            await Gate.WaitAsync(cancellationToken);
            try
            {
                if (_last is { } fresh && DateTime.UtcNow - fresh.At < CacheFor) return fresh.Result;

                var result = await Task.Run(Probe, cancellationToken);
                _last = (DateTime.UtcNow, result);
                return result;
            }
            finally
            {
                Gate.Release();
            }
        }

        private HealthCheckResult Probe()
        {
            var connectionString = configuration.GetConnectionString("RabbitMQ");
            if (string.IsNullOrWhiteSpace(connectionString))
                return HealthCheckResult.Unhealthy("No RabbitMQ connection string is configured.");

            try
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(connectionString),
                    AutomaticRecoveryEnabled = false,
                    RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
                };
                using var connection = factory.CreateConnection("bitween-health");
                connection.Close();
                return HealthCheckResult.Healthy();
            }
            catch (Exception ex)
            {
                // The exception type only: its message can carry the broker address and user.
                return HealthCheckResult.Unhealthy($"RabbitMQ is not reachable ({ex.GetType().Name}).");
            }
        }
    }
}
