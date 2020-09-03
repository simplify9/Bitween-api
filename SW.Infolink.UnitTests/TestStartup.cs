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
            using (var scope = app.ApplicationServices.CreateScope())
            {
                var ctxt = scope.ServiceProvider.GetRequiredService<InfolinkDbContext>();

                // Create the schema in the database
                ctxt.Database.EnsureCreated();

                var doc1 = new Document(1, "Doc1")
                {
                    PromotedProperties = new Dictionary<string, string>
                    {
                        { "Origin", "Parcel.Origin"},
                        { "Destination", "Parcel.Destination"}
                    },
                    BusEnabled = true,
                    BusMessageTypeName = "testMessageType"
                };
                ctxt.Add(doc1);

                var doc22 = new Document(22, "Doc22");
                ctxt.Add(doc22);

                var doc3 = new Document(3, "Doc3")
                {
                    PromotedProperties = new Dictionary<string, string>
                    {
                        { "ConsigneeCountry", "Parcel.Consignee.Address.Country" }
                    },
                    BusEnabled = true,
                    BusMessageTypeName = "testMessageType3"
                };
                ctxt.Add(doc3);

                //var adp = new Adapter(AdapterType.Mapper, "TestAdapter", "");
                //ctxt.Add(adp);

                var sub1 = new Subscription("Test", 1)
                {
                    DocumentFilter = new Dictionary<string, string>
                    {
                        { "Origin", "AMM"},
                        { "Destination", "DXB"}
                    },
                    MapperId = "1"
                };
                ctxt.Add(sub1);

                var sub2 = new Subscription("Test", 1)
                {
                    DocumentFilter = new Dictionary<string, string>
                    {
                        { "Origin", "AMM" },
                    }
                };
                ctxt.Add(sub2);

                var sub3 = new Subscription("Test", 1)
                {
                    DocumentFilter = new Dictionary<string, string>
                    {
                        { "Origin", "LON, PAR, AMS"}
                    }
                };
                ctxt.Add(sub3);

                var sub4 = new Subscription("Test", 22);
                ctxt.Add(sub4);

                var sub5 = new Subscription("Test", 22);
                ctxt.Add(sub5);

                var sub6 = new Subscription("Test", 22);
                ctxt.Add(sub6);

                var sub7 = new Subscription("Test", 3)
                {
                    DocumentFilter = new Dictionary<string, string>
                    {
                        { "ConsigneeCountry", "AE"}
                    }
                };
                ctxt.Add(sub7);

                var rec = new Receiver(1, "test", "2");
                rec.Schedules.Add(new Schedule(Recurrence.Daily, TimeSpan.FromMinutes(5)));
                rec.Schedules.Add(new Schedule(Recurrence.Daily, TimeSpan.FromMinutes(55)));

                ctxt.Add(rec);

                ctxt.SaveChanges();
            }

            app.UseRouting();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
