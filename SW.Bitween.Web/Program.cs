using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using SW.Bitween.Services;
using SW.EfCoreExtensions;
using SW.Logger;
using SW.Logger.ElasticSerach;

namespace SW.Bitween.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            //var id = (long)(DateTime.UtcNow.Subtract(new DateTime(2010, 1, 1)).TotalMilliseconds * 1000);
            var host = CreateHostBuilder(args).UseSwElasticSearchLogger().Build();

            // Startup migration failures otherwise surface only as a bare unhandled exception with
            // no indication of which database was targeted, which makes an environment-specific
            // failure (right code, wrong/stale database) indistinguishable from a code problem.
            try
            {
                host.MigrateDatabase<BitweenDbContext>();
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

            host.ApplyStoredSettings().Run();
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
