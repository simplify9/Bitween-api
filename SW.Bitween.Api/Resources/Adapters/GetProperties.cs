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
    public class GetProperties : IGetHandler<string,object>
    {
        private readonly IServerlessService serverless;
        private readonly NativeAdapterDiscoveryService _nativeAdapterDiscovery;

        public GetProperties(IServerlessService serverless, NativeAdapterDiscoveryService nativeAdapterDiscovery)
        {
            this.serverless = serverless;
            _nativeAdapterDiscovery = nativeAdapterDiscovery;
        }

        async public Task<object> Handle(string key)
        {
            var decodedKey = Uri.UnescapeDataString(key);
            
            // Check if it's a native adapter
            if (decodedKey.StartsWith(NativeAdapterDiscoveryService.NativePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return _nativeAdapterDiscovery.GetExpectedStartupValues(decodedKey);
            }
            
            // Handle serverless adapters
            await serverless.StartAsync(decodedKey, null);
            var expected = await serverless.GetExpectedStartupValues();
            return expected.ToList().ToDictionary(k => k.Key, v => $"{v.Key} {(v.Value.Optional ? $" ({v.Value.Default ?? "null"})" : " *")}");
        }
    }

}
