using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.NativeAdapters.HttpReceiver;
using SW.Bitween.NativeAdapters.Pop3Receiver;
using SW.Bitween.NativeAdapters.RebexPop3Receiver;

namespace SW.Bitween.NativeAdapters;

public static class ServiceCollectionExtensions
{
    public static void AddNativeAdapters(this IServiceCollection serviceCollection, string? rebexLicenseKey = null)
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

        serviceCollection.AddScoped<INativeInfolinkMapper, NativeJSONMapper>();
        serviceCollection.AddScoped<INativeAdapter, NativeJSONMapper>();

        serviceCollection.AddScoped<INativeInfolinkReceiver, NativeHttpReceiver>();
        serviceCollection.AddScoped<INativeAdapter, NativeHttpReceiver>();

        serviceCollection.AddScoped<INativeInfolinkReceiver, NativePop3Receiver>();
        serviceCollection.AddScoped<INativeAdapter, NativePop3Receiver>();

        if (!string.IsNullOrEmpty(rebexLicenseKey))
        {
            serviceCollection.AddScoped<INativeInfolinkReceiver>(_ => new NativeRebexPop3Receiver(rebexLicenseKey));
            serviceCollection.AddScoped<INativeAdapter>(_ => new NativeRebexPop3Receiver(rebexLicenseKey));
        }
    }
}