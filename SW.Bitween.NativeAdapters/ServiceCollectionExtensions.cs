using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.NativeAdapters.AzureBlobReceiver;
using SW.Bitween.NativeAdapters.AzureBlobUploadHandler;
using SW.Bitween.NativeAdapters.HttpReceiver;
using SW.Bitween.NativeAdapters.Pop3Receiver;
using SW.Bitween.NativeAdapters.FileTransfers;
using SW.Bitween.NativeAdapters.RebexFtpReceiver;
using SW.Bitween.NativeAdapters.RebexFtpUploadHandler;
using SW.Bitween.NativeAdapters.RebexPop3Receiver;
using SW.Bitween.NativeAdapters.S3Receiver;
using SW.Bitween.NativeAdapters.S3UploadHandler;
using SW.Bitween.NativeAdapters.SmtpHandler;

namespace SW.Bitween.NativeAdapters;

public static class ServiceCollectionExtensions
{
    /// <param name="rebexLicenseKey">
    /// Reads the current Rebex license key. Called per adapter instance rather than once here,
    /// because the key is a setting that can be changed while the app is running — pasting one in
    /// makes the Rebex adapters usable without a restart.
    /// </param>
    /// <param name="blockPrivateNetworks">
    /// Whether the HTTP adapters may reach private and loopback addresses; see
    /// <see cref="OutboundAddressGuard"/>. Link-local addresses are refused either way.
    /// </param>
    public static void AddNativeAdapters(this IServiceCollection serviceCollection,
        Func<IServiceProvider, string?>? rebexLicenseKey = null,
        Func<IServiceProvider, bool>? blockPrivateNetworks = null)
    {
        serviceCollection.ConfigureHttpClientDefaults(builder =>
        {
            builder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 100
            });
        });
        // The adapters' own client, which carries the address guard. Named rather than set on the
        // defaults above so the guard covers what adapters call and nothing else in the process.
        serviceCollection.AddHttpClient(DynamicHttpProxy.ClientName)
            .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 100,
                ConnectCallback = OutboundAddressGuard.ConnectCallback(blockPrivateNetworks?.Invoke(sp) ?? false)
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

        serviceCollection.AddScoped<INativeInfolinkMapper, Mapper.NativeMapper>();
        serviceCollection.AddScoped<INativeAdapter, Mapper.NativeMapper>();

        serviceCollection.AddScoped<INativeInfolinkReceiver, NativeHttpReceiver>();
        serviceCollection.AddScoped<INativeAdapter, NativeHttpReceiver>();

        serviceCollection.AddScoped<INativeInfolinkReceiver, NativePop3Receiver>();
        serviceCollection.AddScoped<INativeAdapter, NativePop3Receiver>();

        serviceCollection.AddScoped<INativeInfolinkHandler, NativeSmtpHandler>();
        serviceCollection.AddScoped<INativeAdapter, NativeSmtpHandler>();

        serviceCollection.AddScoped<INativeInfolinkHandler, NativeS3UploadHandler>();
        serviceCollection.AddScoped<INativeAdapter, NativeS3UploadHandler>();

        serviceCollection.AddScoped<INativeInfolinkReceiver, NativeS3Receiver>();
        serviceCollection.AddScoped<INativeAdapter, NativeS3Receiver>();

        serviceCollection.AddScoped<INativeInfolinkHandler, NativeAzureBlobUploadHandler>();
        serviceCollection.AddScoped<INativeAdapter, NativeAzureBlobUploadHandler>();

        serviceCollection.AddScoped<INativeInfolinkReceiver, NativeAzureBlobReceiver>();
        serviceCollection.AddScoped<INativeAdapter, NativeAzureBlobReceiver>();

        // SFTP, and FTP/FTPS, with open-source clients: always offered, no license needed.
        serviceCollection.AddScoped<INativeInfolinkHandler, NativeSftpUploadHandler>();
        serviceCollection.AddScoped<INativeAdapter, NativeSftpUploadHandler>();
        serviceCollection.AddScoped<INativeInfolinkReceiver, NativeSftpReceiver>();
        serviceCollection.AddScoped<INativeAdapter, NativeSftpReceiver>();

        serviceCollection.AddScoped<INativeInfolinkHandler, NativeFtpUploadHandler>();
        serviceCollection.AddScoped<INativeAdapter, NativeFtpUploadHandler>();
        serviceCollection.AddScoped<INativeInfolinkReceiver, NativeFtpReceiver>();
        serviceCollection.AddScoped<INativeAdapter, NativeFtpReceiver>();

        if (rebexLicenseKey is not null)
        {
            serviceCollection.AddScoped<INativeInfolinkReceiver>(sp => new NativeRebexPop3Receiver(rebexLicenseKey(sp)));
            serviceCollection.AddScoped<INativeAdapter>(sp => new NativeRebexPop3Receiver(rebexLicenseKey(sp)));

            serviceCollection.AddScoped<INativeInfolinkHandler>(sp => new NativeRebexFtpUploadHandler(rebexLicenseKey(sp)));
            serviceCollection.AddScoped<INativeAdapter>(sp => new NativeRebexFtpUploadHandler(rebexLicenseKey(sp)));

            serviceCollection.AddScoped<INativeInfolinkReceiver>(sp => new NativeRebexFtpReceiver(rebexLicenseKey(sp)));
            serviceCollection.AddScoped<INativeAdapter>(sp => new NativeRebexFtpReceiver(rebexLicenseKey(sp)));
        }
    }
}