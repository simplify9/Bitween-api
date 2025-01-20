//using System;
//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.DependencyInjection;

//namespace SW.Bitween
//{
//    public static class IServiceCollectionExtensions
//    {
//        public static IServiceCollection AddBitween(this IServiceCollection services, Action<BitweenOptions> configure = null)
//        {
//            var BitweenOptions = new BitweenOptions();
//            if (configure != null) configure.Invoke(BitweenOptions);
//            services.BuildServiceProvider().GetRequiredService<IConfiguration>().GetSection(BitweenOptions.ConfigurationSection).Bind(BitweenOptions);
//            services.AddSingleton(BitweenOptions);

//            services.AddSingleton<FilterService>();
//            services.AddScoped<XchangeService>();

//            return services;
//        }

//        public static IServiceCollection AddBitweenHostedServices(this IServiceCollection services, IConfiguration config = null)
//        {
//            services.AddHostedService<AggregationService>();
//            services.AddHostedService<ReceivingService>();

//            return services;
//        }
//    }
//}
