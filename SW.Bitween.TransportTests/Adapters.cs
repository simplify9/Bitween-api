using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.NativeAdapters;

namespace SW.Bitween.TransportTests;

/// <summary>Native adapters as the app builds them: registered by AddNativeAdapters, configured by settings.</summary>
public static class Adapters
{
    static readonly ServiceProvider Services = Build();

    static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddNativeAdapters();
        return services.BuildServiceProvider();
    }

    public static T Create<T>(IDictionary<string, string> settings) where T : INativeAdapter
    {
        var scope = Services.CreateScope();
        var adapter = scope.ServiceProvider.GetServices<INativeAdapter>().OfType<T>().First();
        adapter.InitializeStartupValues(settings);
        return adapter;
    }
}
