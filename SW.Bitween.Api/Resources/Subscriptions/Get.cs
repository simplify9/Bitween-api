using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using SW.EfCoreExtensions;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Model;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace SW.Bitween.Resources.Subscriptions
{
    public class Get : IGetHandler<int, object>
    {
        private readonly BitweenDbContext dbContext;
        private readonly NativeAdapterDiscoveryService _nativeAdapterDiscovery;
        private readonly IServiceProvider _serviceProvider;

        private const string PrivateSentinel = "__private__";

        public Get(BitweenDbContext dbContext, NativeAdapterDiscoveryService nativeAdapterDiscovery, IServiceProvider serviceProvider)
        {
            this.dbContext = dbContext;
            _nativeAdapterDiscovery = nativeAdapterDiscovery;
            _serviceProvider = serviceProvider;
        }

        public async Task<object> Handle(int key)
        {
            var subscriber =
                await dbContext.Set<Subscription>().AsNoTracking().Search("Id", key).SingleOrDefaultAsync();

            var response =
                new SubscriptionGet
                {
                    AggregationForId = subscriber.AggregationForId,
                    DocumentFilter = subscriber.DocumentFilter.ToKeyAndValueCollection(),
                    DocumentId = subscriber.DocumentId,
                    HandlerId = subscriber.HandlerId,
                    Inactive = subscriber.Inactive,
                    MapperId = subscriber.MapperId,
                    ReceiverId = subscriber.ReceiverId,
                    Name = subscriber.Name,
                    PartnerId = subscriber.PartnerId,
                    MapperProperties = subscriber.MapperProperties.ToKeyAndValueCollection(),
                    HandlerProperties = subscriber.HandlerProperties.ToKeyAndValueCollection(),
                    ReceiverProperties = subscriber.ReceiverProperties.ToKeyAndValueCollection(),
                    ValidatorProperties = subscriber.ValidatorProperties.ToKeyAndValueCollection(),
                    Type = subscriber.Type,
                    Temporary = subscriber.Temporary,
                    ResponseSubscriptionId = subscriber.ResponseSubscriptionId,
                    ReceiveOn = subscriber.ReceiveOn,
                    AggregateOn = subscriber.AggregateOn,
                    ConsecutiveFailures = subscriber.ConsecutiveFailures,
                    LastException = subscriber.LastException,
                    AggregationTarget = subscriber.AggregationTarget,
                    ValidatorId = subscriber.ValidatorId,
                    PausedOn = subscriber.PausedOn,
                    MatchExpression = subscriber.MatchExpression,
                    CategoryDescription = subscriber.Category?.Description,
                    CategoryCode = subscriber.Category?.Code,
                    CategoryId = subscriber.CategoryId,
                    WorkGroupId = subscriber.WorkGroupId,
                    RetryPolicyId = subscriber.RetryPolicyId,
                    CustomRetryPolicy = subscriber.CustomRetryPolicy,
                    Schedules = subscriber.Schedules.Select(s => new ScheduleView
                    {
                        Backwards = s.Backwards,
                        Recurrence = s.Recurrence,
                        Days = s.On.Days,
                        Hours = s.On.Hours,
                        Minutes = s.On.Minutes
                    }).ToList()
                };

            response.HandlerProperties = await MaskPrivateProps(subscriber.HandlerId, response.HandlerProperties);
            response.MapperProperties = await MaskPrivateProps(subscriber.MapperId, response.MapperProperties);
            response.ReceiverProperties = await MaskPrivateProps(subscriber.ReceiverId, response.ReceiverProperties);
            response.ValidatorProperties = await MaskPrivateProps(subscriber.ValidatorId, response.ValidatorProperties);

            return response;
        }

        private async Task<ICollection<KeyAndValue>> MaskPrivateProps(string adapterId, ICollection<KeyAndValue> properties)
        {
            if (string.IsNullOrEmpty(adapterId) || properties == null || !properties.Any())
                return properties;

            IDictionary<string, StartupValue> startupValues;

            try
            {
                if (adapterId.StartsWith(NativeAdapterDiscoveryService.NativePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    startupValues = _nativeAdapterDiscovery.GetStartupValues(adapterId);
                }
                else
                {
                    var serverless = _serviceProvider.GetRequiredService<IServerlessService>();
                    await serverless.StartAsync(adapterId, null);
                    startupValues = await serverless.GetExpectedStartupValues();
                }
            }
            catch
            {
                // Fail closed: mask every property value so secrets are never leaked
                // when adapter metadata cannot be retrieved.
                return properties
                    .Select(kv => new KeyAndValue { Key = kv.Key, Value = PrivateSentinel })
                    .ToList();
            }

            return properties.Select(kv =>
            {
                if (startupValues.TryGetValue(kv.Key, out var sv) && sv.Private && !string.IsNullOrEmpty(kv.Value))
                    return new KeyAndValue { Key = kv.Key, Value = PrivateSentinel };
                return kv;
            }).ToList();
        }
    }
}

// using Microsoft.EntityFrameworkCore;
// using SW.Bitween.Domain;
// using SW.PrimitiveTypes;
// using SW.EfCoreExtensions;
// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Text;
// using System.Threading.Tasks;
// using SW.Bitween.Model;
//
// namespace SW.Bitween.Resources.Subscriptions
// {
//     class Get : IGetHandler<int>
//     {
//         private readonly BitweenDbContext dbContext;
//         private readonly IServerlessService serverless1, serverless2, serverless3, serverless4;
//         private readonly BitweenOptions _options;
//
//         public Get(BitweenDbContext dbContext, IServerlessService serverless1, IServerlessService serverless2,
//             IServerlessService serverless3, IServerlessService serverless4, BitweenOptions options)
//         {
//             this.dbContext = dbContext;
//             this.serverless1 = serverless1;
//             this.serverless2 = serverless2;
//             this.serverless3 = serverless3;
//             this.serverless4 = serverless4;
//             _options = options;
//         }
//
//         private static async Task<IDictionary<string, StartupValue>> GetExpectedStartupProperties(string key,
//             IServerlessService serverlessService)
//         {
//             if (string.IsNullOrEmpty(key))
//                 return new Dictionary<string, StartupValue>();
//
//             await serverlessService.StartAsync(key, null);
//             return await serverlessService.GetExpectedStartupValues();
//         }
//
//         private  ICollection<KeyAndValue> GetValuesAndHidePrivate(IDictionary<string, StartupValue> props,
//             IReadOnlyDictionary<string, string> values)
//         {
//             return props.Where(i => values.ContainsKey(i.Key)).Select(kvp =>
//             {
//                 var propValue = values.SafeGetValue(kvp.Key.ToString());
//                 return new KeyAndValue
//                 {
//                     Key = kvp.Key.ToString(),
//                     Value = $"encrypted__{AESCryptoService.Encrypt(propValue, _options.AESEncryptionKey)}"
//                 };
//             }).ToList();
//         }
//
//         public async Task<object> Handle(int key, bool lookup = false)
//         {
//             var subscriber =
//                 await dbContext.Set<Subscription>().AsNoTracking().Search("Id", key)
//                     .SingleOrDefaultAsync();
//
//
//             var mapperProperties = GetExpectedStartupProperties(subscriber.MapperId, serverless1);
//             var handlerProperties = GetExpectedStartupProperties(subscriber.HandlerId, serverless2);
//             var receiverProperties = GetExpectedStartupProperties(subscriber.ReceiverId, serverless3);
//             var validatorProperties = GetExpectedStartupProperties(subscriber.ValidatorId, serverless4);
//
//             await Task.WhenAll(new[] { mapperProperties, handlerProperties, receiverProperties, validatorProperties });
//
//
//             return
//                 new SubscriptionUpdate
//                 {
//                     AggregationForId = subscriber.AggregationForId,
//                     DocumentFilter = subscriber.DocumentFilter.ToKeyAndValueCollection(),
//                     DocumentId = subscriber.DocumentId,
//                     HandlerId = subscriber.HandlerId,
//                     Inactive = subscriber.Inactive,
//                     MapperId = subscriber.MapperId,
//                     ReceiverId = subscriber.ReceiverId,
//                     Name = subscriber.Name,
//                     PartnerId = subscriber.PartnerId,
//                     MapperProperties = GetValuesAndHidePrivate(mapperProperties.Result, subscriber.MapperProperties),
//                     HandlerProperties = GetValuesAndHidePrivate(handlerProperties.Result, subscriber.HandlerProperties),
//                     ReceiverProperties =
//                         GetValuesAndHidePrivate(receiverProperties.Result, subscriber.ReceiverProperties),
//                     ValidatorProperties =
//                         GetValuesAndHidePrivate(validatorProperties.Result, subscriber.ValidatorProperties),
//                     Type = subscriber.Type,
//                     Temporary = subscriber.Temporary,
//                     ResponseSubscriptionId = subscriber.ResponseSubscriptionId,
//                     ReceiveOn = subscriber.ReceiveOn,
//                     AggregateOn = subscriber.AggregateOn,
//                     ConsecutiveFailures = subscriber.ConsecutiveFailures,
//                     LastException = subscriber.LastException,
//                     AggregationTarget = subscriber.AggregationTarget,
//                     ValidatorId = subscriber.ValidatorId,
//                     PausedOn = subscriber.PausedOn,
//                     Schedules = subscriber.Schedules.Select(s => new ScheduleView
//                     {
//                         Backwards = s.Backwards,
//                         Recurrence = s.Recurrence,
//                         Days = s.On.Days,
//                         Hours = s.On.Hours,
//                         Minutes = s.On.Minutes
//                     }).ToList()
//                 };
//         }
//     }
//}