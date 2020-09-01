using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using SW.EfCoreExtensions;
using SW.Infolink;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Infolink.UnitTests
{
    [TestClass]
    public class ServiceTests
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
        public void TestSettings()
        {
            var settings = server.Services.GetService<InfolinkSettings>();

            Assert.IsNotNull(settings);
        }

        [TestMethod]
        public void TestFilterDataService()
        {
            var fs = server.Services.GetService<FilterService>();
            var xf = new XchangeFile(File.ReadAllText("./sampledoc.json"));
            var res =fs.Filter(1, xf).Count();
            Assert.AreNotEqual(0, fs.Filter(1, xf).Count());
        }

        [TestMethod]
        public void TestFilterDataServicePartner()
        {
            var fs = server.Services.GetService<FilterService>();
            var xf = new XchangeFile(File.ReadAllText("./sampledoc2.json"));
            var res = fs.Filter(3, xf).Count();
            Assert.AreNotEqual(0, fs.Filter(3, xf).Count());


        }
        [TestMethod]
        public void TestFilterDataServiceWithNoPromotion()
        {
            var fs = server.Services.GetService<FilterService>();
            var xf = new XchangeFile(File.ReadAllText("./sampledoc.json"));

            Assert.AreNotEqual(0, fs.Filter(22, xf).Count());

        }
        [TestMethod]
        public async Task RetryXchange()
        {
            using var scope = server.Services.CreateScope();
            var xchangeService = scope.ServiceProvider.GetService<XchangeService>();
            //await xchangeService.Retry(23);            

        }


        //[TestMethod]
        //public async Task TestMailBoxService()
        //{
        //    using var scope = server.Services.CreateScope();
        //    var ctxt = scope.ServiceProvider.GetService<InfolinkDbContext>();

        //    //var d = new Document(1, "Doc1");
        //    //ctxt.Add(d);

        //    var k = new AccessKeySet("test", "1234", "5678");
        //   await ctxt.AddAsync(k);

        //    var s = new Subscriber("Test", 1)
        //    {
        //        KeySetId = 1
        //    };
        //   await ctxt.AddAsync(s);
        //   await ctxt.SaveChangesAsync();

        //    var xsvc = scope.ServiceProvider.GetService<XchangeService>();

        //    var xchangeid= await xsvc.Submit(s.Id, new XchangeFile("test"));

        //    //var msvc = services.GetService<MailboxService>();
        //    var cred = new SubscriptionCredential(s.Id, "1234");
        //    await ValidateSubscriber(cred, ctxt);

        //    var xs = await ctxt.ListAsync(new XchangesByDateAndDeliveryFlag(cred.SubscriberId, DateTime.UtcNow.AddDays(-1),null));

        //    var arr =xs.Select(e => e.Id).ToArray();
        //    //var arr = ctxt.Set<XchangeMailboxItem>().Listas(cred, DateTime.UtcNow.AddDays(-1));

        //    //var doc = await msvc.Get(cred, xchangeid);
        //}

        //async Task ValidateSubscriber(SubscriptionCredential credential, DbContext dbContext)
        //{
        //    var sub = await dbContext.FindAsync<Subscriber>(credential.SubscriberId);
        //    if (sub == null) throw new InfolinkException();
        //    if (sub.KeySetId == 0) throw new InfolinkException();

        //    var ks = await dbContext.ListAsync(new AccessKeySetyByKey(sub.KeySetId, credential.Key));

        //    if (ks.Count != 1) throw new InfolinkException();
        //}

        [TestMethod]
        public async Task TestSubmitXchangeScheduledMonthly()
        {
            using var scope = server.Services.CreateScope();
            var ctxt = scope.ServiceProvider.GetService<InfolinkDbContext>();

            //var d = new Document(1, "Doc1");
            //ctxt.Add(d);


            //monthly
            var s = new Subscriber("Test", 1);
            var scheds = new List<string>();
            //scheds.Add("14.15:30:00");
            //scheds.Add("17.15:30:00");
            //scheds.Add("0.15:30:00");
            
            //var schedsarr = scheds.ToArray();
            //var timeSpans = schedsarr.Select(e => TimeSpan.Parse(e)).ToArray();
            var sched = new Schedule(Recurrence.Monthly, TimeSpan.Parse("0.15:30:00"));
            s.Schedules.Add(sched);
            ctxt.Add(s);

            
            
           await ctxt.SaveChangesAsync();

            var xsvc = scope.ServiceProvider.GetService<XchangeService>();

           await  xsvc.Submit(s.Id, new XchangeFile("test"));



            
        }

        [TestMethod]
        public async Task TestSubmitXchangeScheduledWeekly()
        {
            using var scope = server.Services.CreateScope();
            var ctxt = scope.ServiceProvider.GetService<InfolinkDbContext>();

            //weekly
            var s = new Subscriber("Test", 1);
            var scheds = new List<string>();
            
            //scheds.Add("6.15:30:00");
            //scheds.Add("4.15:30:00");
            //scheds.Add("5.15:30:00");
            //scheds.Add("0.15:30:00");
            //var schedsarr = scheds.ToArray();
            //var timeSpans = schedsarr.Select(e => TimeSpan.Parse(e)).ToArray();
            var sched = new Schedule(Recurrence.Weekly, TimeSpan.Parse("4.15:30:00"));
            s.Schedules.Add(sched);
           await ctxt.AddAsync(s);

           await ctxt.SaveChangesAsync();

            var xsvc = scope.ServiceProvider.GetService<XchangeService>();

           await  xsvc.Submit(s.Id, new XchangeFile("test"));
        }
        [TestMethod]
        public async Task TestJsonAggregation()
        {
            JArray jArray = null;
            jArray = new JArray();

            var firstjson = "[{\"payload\":{\"number\":{\"scheme\":\"default\",\"value\":\"\"},\"references\":[{\"type\":\"reftype\",\"value\":\"refval\"}],\"product\":\"STD\",\"ship_date\":\"2019-01-25T05:44:12.251Z\",\"shipper\":{\"name\":\"string\",\"phones\":[\"787988\"],\"emails\":[\"string\"],\"address\":{\"country\":\"US\",\"city\":\"string\",\"state\":\"string\",\"street\":[\"string\"],\"post_code\":\"string\"}},\"consignee\":{\"name\":\"string\",\"phones\":[\"4234234\"],\"emails\":[\"string\"],\"address\":{\"country\":\"AE\",\"city\":\"string\",\"state\":\"string\",\"street\":[\"string\"],\"post_code\":\"string\"}},\"account\":{\"number\":\"0001\",\"entity\":\"DXB\"},\"weight\":{\"value\":1,\"unit\":\"gm\"},\"charge_weight\":{\"value\":1,\"unit\":\"gm\"},\"dimensions\":{\"length\":0,\"width\":0,\"height\":0,\"unit\":\"cm\"},\"items\":[{\"weight\":{\"value\":1,\"unit\":\"gm\"},\"dimensions\":{\"length\":0,\"width\":0,\"height\":0,\"unit\":\"cm\"},\"customs_value\":{\"amount\":1,\"currency\":\"USD\"},\"origin_country\":\"US\",\"description\":\"string\",\"hs_code\":\"string\",\"quantity\":1}]}},{\"payload\":{\"number\":{\"scheme\":\"default\",\"value\":\"\"},\"references\":[{\"type\":\"reftype\",\"value\":\"refval\"}],\"product\":\"STD\",\"ship_date\":\"2019-09-25T05:44:12.251Z\",\"shipper\":{\"name\":\"string\",\"phones\":[\"0797439948\"],\"emails\":[\"string\"],\"address\":{\"country\":\"US\",\"city\":\"string\",\"state\":\"string\",\"street\":[\"string\"],\"post_code\":\"string\"}},\"consignee\":{\"name\":\"string\",\"phones\":[\"0797439948\"],\"emails\":[\"string\"],\"address\":{\"country\":\"AE\",\"city\":\"string\",\"state\":\"string\",\"street\":[\"string\"],\"post_code\":\"string\"}},\"account\":{\"number\":\"0001\",\"entity\":\"DXB\"},\"weight\":{\"value\":1,\"unit\":\"gm\"},\"charge_weight\":{\"value\":1,\"unit\":\"gm\"},\"dimensions\":{\"length\":0,\"width\":0,\"height\":0,\"unit\":\"cm\"},\"items\":[{\"weight\":{\"value\":1,\"unit\":\"gm\"},\"dimensions\":{\"length\":0,\"width\":0,\"height\":0,\"unit\":\"cm\"},\"customs_value\":{\"amount\":1,\"currency\":\"USD\"},\"origin_country\":\"US\",\"description\":\"string\",\"hs_code\":\"string\",\"quantity\":1}]}}]";
            var secjson = "{\"payload\":{\"number\":{\"scheme\":\"default\",\"value\":\"\"},\"references\":[{\"type\":\"reftype\",\"value\":\"refval\"}],\"product\":\"STD\",\"ship_date\":\"2019-01-25T05:44:12.251Z\",\"shipper\":{\"name\":\"string\",\"phones\":[\"787988\"],\"emails\":[\"string\"],\"address\":{\"country\":\"US\",\"city\":\"string\",\"state\":\"string\",\"street\":[\"string\"],\"post_code\":\"string\"}},\"consignee\":{\"name\":\"string\",\"phones\":[\"4234234\"],\"emails\":[\"string\"],\"address\":{\"country\":\"AE\",\"city\":\"string\",\"state\":\"string\",\"street\":[\"string\"],\"post_code\":\"string\"}},\"account\":{\"number\":\"0001\",\"entity\":\"DXB\"},\"weight\":{\"value\":1,\"unit\":\"gm\"},\"charge_weight\":{\"value\":1,\"unit\":\"gm\"},\"dimensions\":{\"length\":0,\"width\":0,\"height\":0,\"unit\":\"cm\"},\"items\":[{\"weight\":{\"value\":1,\"unit\":\"gm\"},\"dimensions\":{\"length\":0,\"width\":0,\"height\":0,\"unit\":\"cm\"},\"customs_value\":{\"amount\":1,\"currency\":\"USD\"},\"origin_country\":\"US\",\"description\":\"string\",\"hs_code\":\"string\",\"quantity\":1}]}}";
            var thrdjson = "{\"payload\":{\"number\":{\"scheme\":\"default\",\"value\":\"\"},\"references\":[{\"type\":\"reftype\",\"value\":\"refval\"}],\"product\":\"STD\",\"ship_date\":\"2019-01-25T05:44:12.251Z\",\"shipper\":{\"name\":\"string\",\"phones\":[\"0797439948\"],\"emails\":[\"string\"],\"address\":{\"country\":\"US\",\"city\":\"string\",\"state\":\"string\",\"street\":[\"string\"],\"post_code\":\"string\"}},\"consignee\":{\"name\":\"string\",\"phones\":[\"0797439948\"],\"emails\":[\"string\"],\"address\":{\"country\":\"AE\",\"city\":\"string\",\"state\":\"string\",\"street\":[\"string\"],\"post_code\":\"string\"}},\"account\":{\"number\":\"0001\",\"entity\":\"DXB\"},\"weight\":{\"value\":1,\"unit\":\"gm\"},\"charge_weight\":{\"value\":1,\"unit\":\"gm\"},\"dimensions\":{\"length\":0,\"width\":0,\"height\":0,\"unit\":\"cm\"},\"items\":[{\"weight\":{\"value\":1,\"unit\":\"gm\"},\"dimensions\":{\"length\":0,\"width\":0,\"height\":0,\"unit\":\"cm\"},\"customs_value\":{\"amount\":1,\"currency\":\"USD\"},\"origin_country\":\"US\",\"description\":\"string\",\"hs_code\":\"string\",\"quantity\":1}]}}";

            JToken xf = JToken.Parse(secjson);
            JToken xf2 = JToken.Parse(thrdjson);
            JToken xf5 = JToken.Parse(firstjson);

            AddTokensToArray(jArray, xf);
            AddTokensToArray(jArray, xf2);
            AddTokensToArray(jArray, xf5);


            var aggr = jArray.ToString();
            using var scope = server.Services.CreateScope();

            var ctxt = scope.ServiceProvider.GetService<InfolinkDbContext>();

            var xchange = new Xchange(1, 1, new XchangeFile( firstjson));
            var xchange2 = new Xchange(1, 1, new XchangeFile(secjson));
            var xchange3 = new Xchange(1, 1, new XchangeFile(thrdjson));
            //xchange.DeliverOn = DateTime.UtcNow.AddDays(-1);
            //xchange2.DeliverOn = DateTime.UtcNow.AddDays(-1);
            //xchange3.DeliverOn = DateTime.UtcNow.AddDays(-1);

            var s = new Subscriber("Test", 1);
            s.Aggregate = true;
            await  ctxt.AddAsync(s);
            await ctxt.AddAsync(xchange);
            await ctxt.AddAsync(xchange2);
            await ctxt.AddAsync(xchange3);
            await ctxt.SaveChangesAsync();
            //var xsvc = services.GetService<ScheduledXchangeService>();
            //var xsvc1 = services.GetService<XchangeService>();
            //xsvc.Run(new object()).Wait();

        }

        void AddTokensToArray(JArray jArray, JToken jtoken)
        {
            if (jArray != null)
            {
                if (jtoken is JArray)
                {
                    foreach (JToken token in jtoken)
                    {
                        jArray.Add(token);
                    }
                }
                else if (jtoken is JObject)
                {
                    jArray.Add(jtoken);
                }
            }
            
        }

        
    }
}
