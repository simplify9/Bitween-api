using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Adapters
{
    [HandlerName(nameof(GetStartupValues))]
    public class GetStartupValues : IGetHandler<string, IDictionary<string, StartupValue>>
    {
        private readonly AdapterStartupValues startupValues;
        private readonly BitweenDbContext dbContext;
        private readonly RequestContext requestContext;

        public GetStartupValues(AdapterStartupValues startupValues,
            BitweenDbContext dbContext, RequestContext requestContext)
        {
            this.startupValues = startupValues;
            this.dbContext = dbContext;
            this.requestContext = requestContext;
        }

        public async Task<IDictionary<string, StartupValue>> Handle(string key)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Subscriptions.View);

            return AdapterStartupValues.WithoutSecretDefaults(
                await startupValues.DescribeOrNotFound(System.Uri.UnescapeDataString(key)));
        }
    }
}
