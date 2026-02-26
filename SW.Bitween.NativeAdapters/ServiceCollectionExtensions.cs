using Microsoft.Extensions.DependencyInjection;

namespace SW.Bitween.NativeAdapters;

public static class ServiceCollectionExtensions
{
    public static void AddNativeAdapters(this IServiceCollection serviceCollection)
    {
        serviceCollection.ConfigureHttpClientDefaults(builder =>
        {
            builder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 100
            });
        });
        serviceCollection.AddSingleton<DynamicHttpProxy>();
        serviceCollection.AddSingleton<IDynamicHttpProxy>(sp => 
            sp.GetRequiredService<DynamicHttpProxy>());

        serviceCollection.AddHostedService(sp => 
            sp.GetRequiredService<DynamicHttpProxy>());

        serviceCollection.AddScoped<INativeInfolinkHandler, NativeHttpHandler>();
        serviceCollection.AddScoped<INativeAdapter, NativeHttpHandler>();
    }
}