using System;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Collections.Generic;
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

        /// <summary>The data stream the sink writes to; its backing indices are ".ds-{this}-*".</summary>
        public string DataStreamName(string environmentName) =>
            $"logs-{ApplicationName.ToLower()}-{environmentName.ToLower()}";
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

            var serilogLogger = logger.CreateLogger();

            // After CreateLogger, because the sink writes its index template while bootstrapping
            // and the retention setting has to end up on that template.
            if (options.ShipsToElasticsearch(environment.EnvironmentName))
                ApplyRetentionPolicy(options, environment.EnvironmentName);

            services.AddSingleton(options);
            services.AddSerilog(serilogLogger, dispose: true);
            return services;
        }

        /// <summary>
        /// Makes ElasticsearchDeleteIndexAfterDays actually govern how long logs are kept.
        /// <para>
        /// Elasticsearch never deletes anything on its own. The sink writes to a
        /// "logs-{app}-{env}" data stream, and a data stream's backing indices inherit their
        /// retention from the composable index template that created them, not from any setting
        /// applied to the stream itself. The sink bootstraps that template pointing at
        /// Elasticsearch's built-in "logs" policy, which only rolls indices over and has no delete
        /// phase, so without this logs accumulate forever. Writing the setting into the template
        /// covers every index created from here on; the sweep afterwards covers the ones already
        /// on disk, which is what lets an existing deployment adopt a retention policy.
        /// </para>
        /// </summary>
        private static void ApplyRetentionPolicy(BitweenLoggerOptions options, string environmentName)
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

            var stream = options.DataStreamName(environmentName);
            var template = FindTemplateFor(client, stream);
            if (template != null) PointTemplateAtPolicy(client, template, options.PolicyName);

            // Existing backing indices keep whatever policy they were created with.
            Request(client, Elasticsearch.Net.HttpMethod.PUT, $"/.ds-{stream}-*/_settings",
                $@"{{""index.lifecycle.name"":""{options.PolicyName}""}}");
        }

        /// <summary>Raw Elasticsearch call; returns the body, or null when the call failed.</summary>
        private static string Request(
            IElasticClient client, Elasticsearch.Net.HttpMethod method, string path, string body = null)
        {
            var response = client.LowLevel.DoRequest<Elasticsearch.Net.StringResponse>(
                method, path, Elasticsearch.Net.PostData.String(body ?? string.Empty));
            return response.Success ? response.Body : null;
        }

        /// <summary>
        /// The one index template Elasticsearch would actually apply to the sink's data stream.
        /// <para>
        /// Several templates can match a name, but only the highest-priority one is used, so that
        /// is the only one worth editing. Templates Elasticsearch manages itself are skipped
        /// outright: the built-in "logs" template matches "logs-*-*" and therefore covers every
        /// service in the cluster, so writing this application's retention into it would quietly
        /// take over how everyone else's logs expire.
        /// </para>
        /// </summary>
        private static string FindTemplateFor(IElasticClient client, string stream)
        {
            var response = Request(client, Elasticsearch.Net.HttpMethod.GET, "/_index_template");
            if (response == null) return null;

            using var document = JsonDocument.Parse(response);
            if (!document.RootElement.TryGetProperty("index_templates", out var templates))
                return null;

            string winner = null;
            var highest = long.MinValue;

            foreach (var entry in templates.EnumerateArray())
            {
                var template = entry.GetProperty("index_template");

                if (template.TryGetProperty("_meta", out var meta)
                    && meta.TryGetProperty("managed", out var managed)
                    && managed.ValueKind == JsonValueKind.True) continue;

                var patterns = template.GetProperty("index_patterns").EnumerateArray();
                if (!patterns.Any(pattern => MatchesPattern(pattern.GetString(), stream))) continue;

                var priority = template.TryGetProperty("priority", out var p) ? p.GetInt64() : 0;
                if (priority < highest) continue;

                highest = priority;
                winner = entry.GetProperty("name").GetString();
            }

            return winner;
        }

        private static bool MatchesPattern(string pattern, string value)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            var regex = "^" + string.Join(".*", pattern.Split('*').Select(Regex.Escape)) + "$";
            return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Rewrites one template with the retention setting added, leaving the rest of it — the ECS
        /// mappings the sink depends on — exactly as the sink wrote it.
        /// </summary>
        private static void PointTemplateAtPolicy(IElasticClient client, string templateName, string policyName)
        {
            var current = Request(client, Elasticsearch.Net.HttpMethod.GET, $"/_index_template/{templateName}");
            if (current == null) return;

            var root = JsonNode.Parse(current);
            var template = root?["index_templates"]?.AsArray().FirstOrDefault()?["index_template"];
            if (template == null) return;

            var body = template.AsObject();
            var inner = body["template"]?.AsObject();
            if (inner == null)
            {
                inner = new JsonObject();
                body["template"] = inner;
            }

            var indexSettings = inner["settings"]?.AsObject();
            if (indexSettings == null)
            {
                indexSettings = new JsonObject();
                inner["settings"] = indexSettings;
            }

            indexSettings["index.lifecycle.name"] = policyName;

            Request(client, Elasticsearch.Net.HttpMethod.PUT,
                $"/_index_template/{templateName}", body.ToJsonString());
        }
    }
}
