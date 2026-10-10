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
        private readonly AdapterStartupValues startupValues;
        private readonly NativeAdapterDiscoveryService _nativeAdapterDiscovery;
        private readonly BitweenDbContext dbContext;
        private readonly RequestContext requestContext;

        public GetProperties(AdapterStartupValues startupValues, NativeAdapterDiscoveryService nativeAdapterDiscovery,
            BitweenDbContext dbContext, RequestContext requestContext)
        {
            this.startupValues = startupValues;
            _nativeAdapterDiscovery = nativeAdapterDiscovery;
            this.dbContext = dbContext;
            this.requestContext = requestContext;
        }

        async public Task<object> Handle(string key)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);

            var decodedKey = Uri.UnescapeDataString(key);
            
            // Check if it's a native adapter
            if (decodedKey.StartsWith(NativeAdapterDiscoveryService.NativePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return _nativeAdapterDiscovery.GetExpectedStartupValues(decodedKey);
            }
            
            // Handle serverless adapters. The native branch above returns a different shape —
            // each key's default rather than a "key (default)" label — so it is left as it was.
            var expected = AdapterStartupValues.WithoutSecretDefaults(await startupValues.DescribeOrNotFound(decodedKey));

            return expected
                .ToList()
                .ToDictionary(
                    k => k.Key,
                    v => $"{v.Key} {(v.Value.Optional ? $" ({v.Value.Default ?? "null"})" : " *")}");
        }
    }

}
