using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Resources.Adapters
{
    [HandlerName("properties")]
    class GetProperties : IGetHandler<string>
    {
        private readonly InfolinkDbContext dbContext;
        private readonly IServerlessService serverless;

        public GetProperties(InfolinkDbContext dbContext, IServerlessService serverless)
        {
            this.dbContext = dbContext;
            this.serverless = serverless;
        }

        async public Task<object> Handle(string key, bool lookup = false)
        {
            await serverless.StartAsync("infolink.mappers.sample");

            var expected = await serverless.GetExpectedStartupValues();

            return expected.ToList().ToDictionary(k => k.Key, v => v.Key);
        }
    }

}
