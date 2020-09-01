using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

using SW.PrimitiveTypes;
using System.Threading.Tasks;
using SW.Infolink.Domain;
using SW.EfCoreExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore;

namespace SW.Infolink.UnitTests
{

    [TestClass]
    public class SearchServiceTests
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
        }

        //[TestMethod]
        //public async Task TestAdapterSearch()
        //{
        //    using var scope = server.Services.CreateScope();
        //    var ctxt = scope.ServiceProvider.GetService<InfolinkDbContext>();
        //    var Conditions = new List<SearchyCondition> { new SearchyCondition(new List<SearchyFilter> { new SearchyFilter("Id", SearchyRule.EqualsTo, 1) }) };
        //    //var x = await ctxt.Set<Adapter>().Search(Conditions).ToListAsync();
        //}

        [TestMethod]
        public async Task TestXchangeSearch()
        {
            using var scope = server.Services.CreateScope();

            var ctxt = scope.ServiceProvider.GetService<InfolinkDbContext>();

            //var d = new Document(1, "Doc1");
            //ctxt.Add(d);

            //var k = new AccessKeySet("test", "1234", "5678");

            //ctxt.Add(k);

            var s = new Subscriber("Test", 1)
            {
                //KeySetId = 1
            };
            ctxt.Add(s);
            ctxt.SaveChanges();

            var xsvc = scope.ServiceProvider.GetService<XchangeService>();

            xsvc.Submit(s.Id, new XchangeFile("test")).Wait();



            //var searchSvc = services.GetService<ISearchable<XchangeRow>>();
            //SearchyRequest searchyRequest = new SearchyRequest
            //{
            //    Conditions = new List<SearchyCondition> { new SearchyCondition(new List<SearchyFilter> { new SearchyFilter("Id", SearchyRule.EqualsTo, 1) }) }
            //};
            //var x = await searchSvc.Search(searchyRequest);

            //var ctxt = services.GetService<InfolinkDbContext>();
            var Conditions = new List<SearchyCondition> { new SearchyCondition(new List<SearchyFilter> { new SearchyFilter("Id", SearchyRule.EqualsTo, 1) }) };
            var x = await ctxt.Set<Xchange>().Search(Conditions).ToListAsync();

        }
    }
}
