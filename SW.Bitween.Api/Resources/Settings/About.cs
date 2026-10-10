using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SW.PrimitiveTypes;
using SW.Serverless.Runtimes;

namespace SW.Bitween.Resources.Settings;

/// <summary>
/// What this Bitween is and how it is set up, as the node that answers sees it: its version, the
/// node, its database, storage and broker, its readiness checks, which adapter runtimes it has, and
/// the settings that shape behaviour but live in configuration rather than on the Settings page.
/// Secrets are never in it, only whether they are set.
/// </summary>
[HandlerName("about")]
public class About(BitweenDbContext dbContext, RequestContext requestContext, BitweenOptions options,
    IConfiguration configuration, IServiceProvider services) : IQueryHandler<object>
{
    static readonly DateTimeOffset StartedOn = DateTimeOffset.UtcNow;

    public async Task<object> Handle()
    {
        await requestContext.EnsurePermission(dbContext, Model.Permissions.Settings.View);

        var limits = configuration.GetSection("Bitween:RateLimits");
        var runtimes = services.GetService<AdapterRuntimes>();

        return new
        {
            Version = BitweenInfo.Version(options).ToString(),
            Build = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            Node = new
            {
                Name = Environment.MachineName,
                StartedOn,
                Runtime = RuntimeInformation.FrameworkDescription,
                Os = RuntimeInformation.OSDescription,
                DataSources = options.BusProvidersEnabled,
            },
            Database = options.DatabaseType,
            Storage = new { Provider = options.StorageProvider, options.AdapterPath, options.DocumentPrefix },
            Broker = new
            {
                options.QueuePrefix,
                ManagementConfigured = !string.IsNullOrWhiteSpace(options.RabbitMqManagementUrl),
            },
            Health = await HealthAsync(),
            Adapters = new
            {
                Runtimes = runtimes == null
                    ? []
                    : await Task.WhenAll(new[] { "dotnet", "python", "node" }.Select(async name =>
                    {
                        var status = await runtimes.StatusAsync(name);
                        return new { Name = name, status.Available, status.Version, status.Reason };
                    })),
                Pip = await ToolAsync("pip", "python3", "-m", "pip", "--version"),
                Npm = await ToolAsync("npm", "npm", "--version"),
                CommandTimeoutSeconds = options.ServerlessCommandTimeout,
                Editor = new
                {
                    Dependencies = options.AdapterEditorDependencies,
                    MemoryMb = options.AdapterEditorMemoryMb,
                    CpuCores = options.AdapterEditorCpuCores,
                },
            },
            Limits = new
            {
                SignInPerMinute = limits.GetValue("SignInPerMinute", 10),
                RequestsPerMinute = limits.GetValue("RequestsPerMinute", 600),
                FileLinksPerMinute = limits.GetValue("FileLinksPerMinute", 60000),
                options.MaxResponseWaitSeconds,
                options.MaxRetryChainDepth,
                options.StaleRunAfterMinutes,
                options.NotifierQuietMinutes,
            },
            Network = new
            {
                options.PublicUrl,
                options.BlockPrivateNetworkAddresses,
                TrustedProxies = string.IsNullOrWhiteSpace(options.TrustedProxies) ? 0 : options.TrustedProxies.Split(',', ';').Length,
                options.ExposeApiDocs,
                CorsOrigins = options.CorsOrigins ?? [],
            },
            Retention = new
            {
                options.ReceiveAttemptRetentionDays,
                options.ReceiveAttemptCleanupCron,
                options.InboundMessagePruneCron,
            },
            Telemetry = new
            {
                OpenTelemetry = !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? configuration["OpenTelemetry:Endpoint"]),
            },
        };
    }

    /// <summary>The readiness checks /health/ready runs: the database and the broker.</summary>
    async Task<object> HealthAsync()
    {
        var health = services.GetService<HealthCheckService>();
        if (health == null) return Array.Empty<object>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var report = await health.CheckHealthAsync(_ => true, timeout.Token);
        return report.Entries.Select(e => new
        {
            Name = e.Key,
            Status = e.Value.Status.ToString(),
            DurationMs = (int)e.Value.Duration.TotalMilliseconds,
            e.Value.Description,
            Error = e.Value.Exception?.Message,
        }).ToList();
    }

    static readonly Dictionary<string, Lazy<Task<object>>> Tools = new();
    static readonly object ToolsGate = new();

    /// <summary>Whether a tool the adapter editor needs answers, asked once per process.</summary>
    static Task<object> ToolAsync(string key, string file, params string[] arguments)
    {
        lock (ToolsGate)
        {
            if (!Tools.TryGetValue(key, out var lazy))
                Tools[key] = lazy = new Lazy<Task<object>>(() => ProbeAsync(file, arguments));
            return lazy.Value;
        }
    }

    static async Task<object> ProbeAsync(string file, string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0
                ? new { Available = true, Version = output.Trim().Split('\n')[0], Reason = (string)null }
                : new { Available = false, Version = (string)null, Reason = $"exited with {process.ExitCode}" };
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException or InvalidOperationException)
        {
            return new { Available = false, Version = (string)null, Reason = $"'{file}' isn't installed on this node" };
        }
    }
}
