using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using SW.Bitween.Services;
using SW.Logger;

namespace SW.Bitween.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            //var id = (long)(DateTime.UtcNow.Subtract(new DateTime(2010, 1, 1)).TotalMilliseconds * 1000);
            IHost host;
            try
            {
                host = CreateHostBuilder(args).Build();
            }
            catch (StartupProblemException ex)
            {
                // No database to check further, and no logger yet: the list is all there is to say.
                Console.Error.WriteLine(ex.Message);
                Environment.ExitCode = 1;
                return;
            }

            // Startup migration failures otherwise surface only as a bare unhandled exception with
            // no indication of which database was targeted, which makes an environment-specific
            // failure (right code, wrong/stale database) indistinguishable from a code problem.
            try
            {
                MigrateDatabase(host);
            }
            catch (Exception ex)
            {
                var connectionString = host.Services.GetRequiredService<IConfiguration>()
                    .GetConnectionString("BitweenDb");
                var b = new NpgsqlConnectionStringBuilder(connectionString);
                host.Services.GetRequiredService<ILogger<Program>>().LogCritical(ex,
                    "Database migration failed on startup against {Host}:{Port}/{Database}",
                    b.Host, b.Port, b.Database);
                throw;
            }

            host.SecureSeededAdministrator().SecureSystemPartnerKey().HashStoredPartnerKeys()
                .MoveRetryAlertsToChannels().ApplyStoredSettings();

            if (RefusesToStart(host)) return;
            host.Run();
        }

        /// <summary>
        /// Logs every problem found while starting as one list, configuration and database alike, and
        /// exits without serving.
        /// </summary>
        private static bool RefusesToStart(IHost host)
        {
            var problems = host.Services.GetRequiredService<StartupProblems>();
            if (!problems.Any) return false;

            host.Services.GetRequiredService<ILogger<Program>>().LogCritical("{Problems}", problems.ToString());
            // Flushes the log before the process ends.
            host.Dispose();
            Environment.ExitCode = 1;
            return true;
        }

        /// <summary>The longest one migration statement may run, such as an index build on a big table.</summary>
        private static readonly TimeSpan MigrationCommandTimeout = TimeSpan.FromMinutes(30);

        /// <remarks>
        /// A migration that builds an index on the exchange tables runs for minutes on a database of
        /// years of traffic, past the 30 seconds a connection string allows a command by default: on a
        /// client's production upgrade it timed out and the pod restarted over and over. The longer
        /// limit is for the migrations only; every other query keeps the connection string's. The
        /// chart's startup probe allows longer than this, so a migration that does run out logs why.
        /// </remarks>
        private static void MigrateDatabase(IHost host)
        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            db.Database.SetCommandTimeout(MigrationCommandTimeout);
            db.Database.Migrate();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                    webBuilder.UseKestrel(options =>
                    {
                        options.Limits.MaxRequestBodySize = 52428800; //50MB
                    });
                });

    }
}
