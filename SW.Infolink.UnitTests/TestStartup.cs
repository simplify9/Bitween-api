using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bus;
using SW.CqApi;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.UnitTests
{
    public class TestStartup
    {

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddInfolink();
            services.AddSingleton<ScheduledXchangeService>();
            services.AddSingleton<IDomainEventDispatcher, MockDispatcher>();
            //services.AddBusPublishMock();
            services.AddCqApi(typeof(InfolinkDbContext).Assembly);

            services.AddControllers().
                AddApplicationPart(typeof(CqApiController).Assembly);
            services.AddAuthorization();
            services.AddAuthentication().
                AddJwtBearer();

            services.AddDbContext<InfolinkDbContext>(builder =>
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
