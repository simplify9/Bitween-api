//using System;
//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.DependencyInjection;

//namespace SW.Infolink
//{
//    public static class IServiceCollectionExtensions
//    {
//        public static IServiceCollection AddInfolink(this IServiceCollection services, Action<InfolinkOptions> configure = null)
//        {
//            var infolinkOptions = new InfolinkOptions();
//            if (configure != null) configure.Invoke(infolinkOptions);
//            services.BuildServiceProvider().GetRequiredService<IConfiguration>().GetSection(InfolinkOptions.ConfigurationSection).Bind(infolinkOptions);
//            services.AddSingleton(infolinkOptions);

//            services.AddSingleton<FilterService>();
//            services.AddScoped<XchangeService>();

//            return services;
//        }

//        public static IServiceCollection AddInfolinkHostedServices(this IServiceCollection services, IConfiguration config = null)
//        {
//            services.AddHostedService<AggregationService>();
//            services.AddHostedService<ReceivingService>();

//            return services;
//        }
//    }
//}
