using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.UnitTests
{
    [TestClass]
    public class BusServiceTests
    {
        static TestServer server;

        [ClassInitialize]
        public static void ClassInitialize(TestContext tcontext)
        {
            server = new TestServer(WebHost.CreateDefaultBuilder()
                .UseEnvironment("Development")
                .UseStartup<TestStartup>());
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            server.Dispose();
        }

        [TestMethod]
        async public Task TestGetMessageTypeNameToDocumentIdMap()
        {

            //var constr = server.Host.Services.GetRequiredService<IConfiguration>().GetConnectionString("RabbitMQ");
            //var memCache = server.Host.Services.GetRequiredService<BusService>();
            //var result = await memCache.GetMessageTypeNameToDocumentIdMap();
            //result = await memCache.GetMessageTypeNameToDocumentIdMap();
        }
    }
}
