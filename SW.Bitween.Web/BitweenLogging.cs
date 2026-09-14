using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Serilog.Sinks;
using Elastic.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nest;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace SW.Bitween.Web
{
    /// <summary>
    /// Options for <see cref="BitweenLogging.AddBitweenLogging"/>, bound from the "SwLogger"
    /// configuration section so the names match what the Helm chart already sets
    /// (SwLogger__ElasticsearchUrl and friends).
    /// </summary>
    public class BitweenLoggerOptions
    {
        public const string ConfigurationSection = "SwLogger";

        /// <summary>Serilog's LogEventLevel: 0 Verbose, 1 Debug, 2 Information, 3 Warning.</summary>
        public int LoggingLevel { get; set; } = 2;

        public string ApplicationName { get; set; } = "unknownapp";
        public string ApplicationVersion { get; set; }

        /// <summary>Unset disables the Elasticsearch sink entirely; stdout is unaffected.</summary>
        public string ElasticsearchUrl { get; set; }

        public string ElasticsearchUser { get; set; }
        public string ElasticsearchPassword { get; set; }

        /// <summary>
        /// Comma-separated environment names that ship to Elasticsearch. An environment absent
        /// from this list logs to stdout only, which is how a given deployment opts out.
        /// </summary>
        public string ElasticsearchEnvironments { get; set; } = "Development,Staging,Production";

        public string ElasticsearchCertificatePath { get; set; }
        public int ElasticsearchDeleteIndexAfterDays { get; set; } = 90;

        public bool ShipsToElasticsearch(string environmentName) =>
            !string.IsNullOrWhiteSpace(ElasticsearchUrl)
            && !string.IsNullOrWhiteSpace(ElasticsearchEnvironments)
            && ElasticsearchEnvironments
                .Split(',')
                .Select(e => e.Trim())
                .Contains(environmentName, StringComparer.OrdinalIgnoreCase);

        public string PolicyName => $"{ApplicationName.ToLower()}-policy";
    }

    /// <summary>
    /// Builds the one Serilog pipeline this service logs through, writing to stdout always and to
    /// Elasticsearch where configured.
    /// <para>
    /// This replaces AddSWConsoleLogger/UseSwElasticSearchLogger rather than calling either.
    /// Both of those build a pipeline of their own and only one can win: the Elasticsearch package
    /// calls UseSerilog with writeToProviders:false, which silently discards the console package's
    /// provider, and its own console sink is hardcoded to plain text. Running both therefore
    /// produced no JSON on stdout at all, so the log collector had nothing structured to index.
    /// One pipeline with two sinks is what actually lets both destinations work at once.
    /// </para>
    /// </summary>
    public static class BitweenLogging
    {
        public static IServiceCollection AddBitweenLogging(
            this IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment,
            Action<BitweenLoggerOptions> configure = null)
        {
            var options = new BitweenLoggerOptions
            {
                ApplicationVersion = Assembly.GetCallingAssembly().GetName().Version?.ToString()
            };
            configure?.Invoke(options);
            // Configuration last, so a deployment's environment variables win over code defaults.
            configuration.GetSection(BitweenLoggerOptions.ConfigurationSection).Bind(options);

            var logger = new LoggerConfiguration()
                .MinimumLevel.Is((LogEventLevel)options.LoggingLevel)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Environment", environment.EnvironmentName)
                .Enrich.WithProperty("ApplicationVersion", options.ApplicationVersion)
                .Enrich.WithProperty("Application", options.ApplicationName);

            // CLEF (compact JSON) is what makes every property queryable once collected. Under a
            // debugger nobody is collecting anything, so prefer the line a human can read.
            logger = Debugger.IsAttached
                ? logger.WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                : logger.WriteTo.Console(new CompactJsonFormatter());

            if (options.ShipsToElasticsearch(environment.EnvironmentName))
            {
                CreateLifeCyclePolicy(options);
                logger = logger.WriteTo.Elasticsearch(
                    new[] { new Uri(options.ElasticsearchUrl) },
                    opts =>
                    {
                        opts.DataStream = new DataStreamName(
                            "logs", options.ApplicationName.ToLower(), environment.EnvironmentName);
                        opts.BootstrapMethod = BootstrapMethod.Failure;
                    },
                    transport =>
                    {
                        transport.Authentication(
                            new BasicAuthentication(options.ElasticsearchUser, options.ElasticsearchPassword));
                        // Only override validation when a custom authority is supplied. Trusting
                        // every certificate would expose these credentials and the log stream to
                        // anyone able to impersonate the Elasticsearch host.
                        if (!string.IsNullOrWhiteSpace(options.ElasticsearchCertificatePath))
                        {
                            transport.ServerCertificateValidationCallback(
                                CertificateValidations.AuthorityIsRoot(
                                    new System.Security.Cryptography.X509Certificates.X509Certificate(
                                        options.ElasticsearchCertificatePath)));
                        }
                    });
            }

            services.AddSingleton(options);
            services.AddSerilog(logger.CreateLogger(), dispose: true);
            return services;
        }

        /// <summary>
        /// Pushes the retention policy, carried over unchanged from SimplyWorks.Logger.ElasticSearch.
        /// <para>
        /// Note that the policy is created but not yet attached to anything: the sink writes to a
        /// "logs-{app}-{env}" data stream whose backing indices are named ".ds-logs-*", so the
        /// pattern below matches no index, and those backing indices inherit Elasticsearch's
        /// built-in "logs" policy instead of this one. Attaching it means owning the sink's
        /// composable index template, which the sink rewrites whenever it bootstraps, so
        /// ElasticsearchDeleteIndexAfterDays does not currently govern retention.
        /// </para>
        /// </summary>
        private static void CreateLifeCyclePolicy(BitweenLoggerOptions options)
        {
            var settings = new ConnectionSettings(new Uri(options.ElasticsearchUrl))
                .BasicAuthentication(options.ElasticsearchUser, options.ElasticsearchPassword);
            var client = new ElasticClient(settings);

            client.IndexLifecycleManagement.PutLifecycle(options.PolicyName, p => p
                .Policy(po => po
                    .Phases(ph => ph
                        .Delete(d => d
                            .MinimumAge($"{options.ElasticsearchDeleteIndexAfterDays}d")
                            .Actions(a => a.Delete(x => x))))));

            client.Indices.UpdateSettings(new UpdateIndexSettingsRequest($"{options.ApplicationName.ToLower()}-*")
            {
                IndexSettings = new IndexSettings { { "index.lifecycle.name", options.PolicyName } }
            });
        }
    }
}
