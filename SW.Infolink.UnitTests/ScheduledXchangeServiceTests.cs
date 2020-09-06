//using Microsoft.AspNetCore;
//using Microsoft.AspNetCore.Hosting;
//using Microsoft.AspNetCore.TestHost;
//using Microsoft.Data.Sqlite;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.VisualStudio.TestTools.UnitTesting;
//using SW.PrimitiveTypes;
//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Text;
//using System.Threading.Tasks;

//namespace SW.Infolink.UnitTests
//{

//    [TestClass]
//    public class ScheduledXchangeServiceTests
//    {

//        static TestServer server;

//        [ClassInitialize]
//        public static void ClassInitialize(TestContext tcontext)
//        {
//            server = new TestServer(WebHost.CreateDefaultBuilder()
//                .UseEnvironment("Development")
//                .UseStartup<TestStartup>());
//        }


//        [TestMethod]
//        public async Task TestRun1()
//        {
//            var sxsvc = server.Services.GetService<ScheduledXchangeService>();
//            var xf = new XchangeFile(File.ReadAllText("./sampledoc.json"));
//            using var scope = server.Services.CreateScope();
//            var xsvc = scope.ServiceProvider.GetService<XchangeService>();
//            for (var i=0; i<10; i++)
//            {
//               await xsvc.Submit(1, xf);
//            }

//            for (var i = 0; i < 10; i++)
//            {
//               await  xsvc.Submit(2, xf);
//            }

//            await sxsvc.Run(DateTime.UtcNow.AddDays(1));
//            //Assert.IsNotNull(settings);
//        }


//    }
//}
