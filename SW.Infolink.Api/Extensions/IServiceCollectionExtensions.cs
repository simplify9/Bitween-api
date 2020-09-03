using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SW.Infolink
{
    public static class IServiceCollectionExtensions
    {
        public static IServiceCollection AddInfolink(this IServiceCollection services, IConfiguration config = null)
        {

            services.AddSingleton<InfolinkSettings>();
            services.AddSingleton<FilterService>();

            //services.AddScoped<PipelineService>();
            services.AddScoped<BlobService>();
            services.AddScoped<XchangeService>();

            return services;
        }

        public static IServiceCollection AddInfolinkHostedServices(this IServiceCollection services, IConfiguration config = null)
        {
            services.AddHostedService<ScheduledXchangeService>();
            services.AddHostedService<ScheduledReceiverService>();

            return services;
        }
    }
}
