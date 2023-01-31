using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.UnitTests
{
    [TestClass]
    public class ApiTests
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
        public Task TestSubmitXchangeFail()
        {
            return Task.CompletedTask;
            //var xs = new XchangeRequest
            //{
            //    SubscriberId = 100000,

            //};

            //var body = JsonConvert.SerializeObject(xs);//, _jsonSettings);
            //using var client = server.CreateClient();
            //var response = await client.PostAsync("/cqapi/xchanges", new StringContent(body, Encoding.UTF8, "application/json"));
            //var responseBody = await response.Content.ReadAsStringAsync();

            //Assert.AreEqual(response.StatusCode, HttpStatusCode.BadRequest);

        }

        [TestMethod]
        public Task TestSubmitXchangeSuccess()
        {
            return Task.CompletedTask;
            //var xs = new XchangeRequest
            //{
            //    SubscriberId = 1,
            //    File = new XchangeFile("test")

            //};

            //var body = JsonConvert.SerializeObject(xs);//, _jsonSettings);
            //using var client = server.CreateClient();
            //var response = await client.PostAsync("/cqapi/xchanges", new StringContent(body, Encoding.UTF8, "application/json"));
            //var responseBody = await response.Content.ReadAsStringAsync();
            //response.EnsureSuccessStatusCode();

            ////var response2 = await client.GetAsync($"/api/xchange/{responseBody}");
            ////var responseBody2 = await response2.Content.ReadAsStringAsync();
            ////response2.EnsureSuccessStatusCode();

        }

        [TestMethod]
        public Task TestDocFilterFail()
        {
            //var xs = new XchangeRequest
            //{
            //    DocumentId = 1,
            //    File = new XchangeFile( "{data: 1}")
            //};

            //var body = JsonConvert.SerializeObject(xs);
            //using var client = server.CreateClient();

            //var response = await client.PostAsync("/cqapi/xchanges", new StringContent(body, Encoding.UTF8, "application/json"));
            //var responseBody = await response.Content.ReadAsStringAsync();

            //Assert.AreEqual(response.StatusCode, HttpStatusCode.BadRequest);
            return Task.CompletedTask;
        }

        //[TestMethod]
        //public async Task TestGetAccessKeySet()
        //{

        //    AccessKeySetConfig accessKeySetConfig = new AccessKeySetConfig { Key1="1",Key2="2" ,Name="test"};
        //    var body = JsonConvert.SerializeObject(accessKeySetConfig);
        //    using var client = server.CreateClient();

        //    await client.PostAsync("/cqapi/accesskeysets", new StringContent(body, Encoding.UTF8, "application/json"));

        //    var response = await client.GetAsync("/cqapi/accesskeysets/1");
        //    var responseBody = await response.Content.ReadAsStringAsync();

        //    Assert.AreEqual(response.StatusCode, HttpStatusCode.OK);

        //    //var getaccessks = serviceProvider.GetService<IGettable<AccessKeySetConfig>>();

        //    //var result = await getaccessks.Get("1");

        //    //var getaccessks1 = serviceProvider.GetService<ILookable<AccessKeySetConfig>>();
        //    //var result1 = await getaccessks1.LookupList(null, new PrimitiveTypes.SearchyRequest("Id", PrimitiveTypes.SearchyRule.EqualsTo, 1));

        //    //var getadapter = serviceProvider.GetService<IGettable<AdapterConfig>>();

        //    //var result2 = await getadapter.Get("1");

        //    //var updateadapter = serviceProvider.GetService<IUpdatable<AdapterConfig>>();

        //    //result2.Description = "test desc";
        //    ////result2.Properties.Add("ss", "sss");

        //    //await updateadapter.Update("1", result2);

        //    //var getreceiver = serviceProvider.GetService<IGettable<ReceiverConfig>>();

        //    //var result3 = await getreceiver.Get("1");

        //    //var updateReceiver = serviceProvider.GetService<IUpdatable<ReceiverConfig>>();
        //    //result3.Schedules.Remove(result3.Schedules.First());
        //    //result3.Schedules.Add(new Schedule(Recurrence.Monthly, TimeSpan.FromMinutes(10)));

        //}

        [TestMethod]
        public void TestValueTypes()
        {
            var file = new XchangeFile("test data", "file.txt");

            var str = JsonConvert.SerializeObject(file);

            var filed = JsonConvert.DeserializeObject<XchangeFile>(str);  
        }


    }
}
