using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Adapters
{
    [HandlerName(nameof(GetStartupValues))]
    public class GetStartupValues : IGetHandler<string, IDictionary<string, StartupValue>>
    {
        private readonly IServerlessService serverless;
        private readonly NativeAdapterDiscoveryService _nativeAdapterDiscovery;
        private readonly BitweenDbContext dbContext;
        private readonly RequestContext requestContext;

        public GetStartupValues(IServerlessService serverless, NativeAdapterDiscoveryService nativeAdapterDiscovery,
            BitweenDbContext dbContext, RequestContext requestContext)
        {
            this.serverless = serverless;
            _nativeAdapterDiscovery = nativeAdapterDiscovery;
            this.dbContext = dbContext;
            this.requestContext = requestContext;
        }



        public async Task<IDictionary<string, StartupValue>> Handle(string key)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);

            var decodedKey = Uri.UnescapeDataString(key);

            IDictionary<string, StartupValue> startupValues = new Dictionary<string, StartupValue>();
            
            // Check if it's a native adapter
            if (decodedKey.StartsWith(NativeAdapterDiscoveryService.NativePrefix, StringComparison.OrdinalIgnoreCase))
            {
                startupValues= _nativeAdapterDiscovery.GetStartupValues(decodedKey);
            }
            else
            {
                    // Handle serverless adapters
                try
                {
                    await serverless.StartAsync(decodedKey, null);
                }
                catch (KeyNotFoundException ex)
                {
                    throw new BitweenException(
                        $"Adapter '{decodedKey}' metadata is incomplete or the adapter package is not installed. " +
                        $"Missing metadata key: {ex.Message}", ex);
                }

                startupValues = await serverless.GetExpectedStartupValues();
            }
            
            return startupValues ?? new Dictionary<string, StartupValue>();
        }
    }

}
