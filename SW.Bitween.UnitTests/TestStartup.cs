using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bus;
using SW.CqApi;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests
{
    public class TestStartup
    {

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            var BitweenOptions = new BitweenOptions();
            services.AddSingleton(BitweenOptions);
            services.AddSingleton<FilterService>();
            services.AddScoped<XchangeService>();

            services.AddCqApi(typeof(BitweenDbContext).Assembly);

            services.AddControllers().
                AddApplicationPart(typeof(CqApiController).Assembly);
            services.AddAuthorization();
            services.AddAuthentication().
                AddJwtBearer();

            services.AddDbContext<BitweenDbContext>(builder =>
            {
                var _connection = new SqliteConnection("DataSource=:memory:");
                _connection.Open();

                builder
                   .UseSqlite(_connection)
                   .EnableSensitiveDataLogging(true);
            },
            ServiceLifetime.Scoped,
            ServiceLifetime.Singleton);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {

            app.UseRouting();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
