using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Adapters
{
    [HandlerName("properties")]
    class GetProperties : IGetHandler<string,object>
    {
        private readonly IServerlessService serverless;

        public GetProperties(IServerlessService serverless)
        {
            this.serverless = serverless;
        }

        async public Task<object> Handle(string key)
        {
            await serverless.StartAsync( Uri.UnescapeDataString(key), null);
            var expected = await serverless.GetExpectedStartupValues();
            return expected.ToList().ToDictionary(k => k.Key, v => $"{v.Key} {(v.Value.Optional ? $" ({v.Value.Default ?? "null"})" : " *")}");
        }
    }

}
