using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using SW.EfCoreExtensions;
using System.Linq;
using System.Threading.Tasks;
using SW.Infolink.Model;

namespace SW.Infolink.Resources.Subscriptions
{
    class Get : IGetHandler<int>
    {
        private readonly InfolinkDbContext dbContext;

        public Get(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        public async Task<object> Handle(int key, bool lookup = false)
        {
            var subscriber =
                await dbContext.Set<Subscription>().AsNoTracking().Search("Id", key).SingleOrDefaultAsync();

            return
                new SubscriptionUpdate
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
                    Schedules = subscriber.Schedules.Select(s => new ScheduleView
                    {
                        Backwards = s.Backwards,
                        Recurrence = s.Recurrence,
                        Days = s.On.Days,
                        Hours = s.On.Hours,
                        Minutes = s.On.Minutes
                    }).ToList()
                };
        }
    }
}

// using Microsoft.EntityFrameworkCore;
// using SW.Infolink.Domain;
// using SW.PrimitiveTypes;
// using SW.EfCoreExtensions;
// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Text;
// using System.Threading.Tasks;
// using SW.Infolink.Model;
//
// namespace SW.Infolink.Resources.Subscriptions
// {
//     class Get : IGetHandler<int>
//     {
//         private readonly InfolinkDbContext dbContext;
//         private readonly IServerlessService serverless1, serverless2, serverless3, serverless4;
//         private readonly InfolinkOptions _options;
//
//         public Get(InfolinkDbContext dbContext, IServerlessService serverless1, IServerlessService serverless2,
//             IServerlessService serverless3, IServerlessService serverless4, InfolinkOptions options)
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