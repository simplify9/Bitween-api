using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Newtonsoft.Json;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink
{
    static class ChangeTrackerExtensions
    {
        async public static Task PublishEvents(this ChangeTracker changeTracker, IPublish publish)
        {
            var entitiesWithEvents = changeTracker.Entries<IGeneratesDomainEvents>()
                .Select(e => e.Entity)
                .Where(e => e.Events.Any())
                .ToArray();

            foreach (var entity in entitiesWithEvents)
            {
                var events = entity.Events.ToArray();
                entity.Events.Clear();
                foreach (var domainEvent in events)
                {
                    await publish.Publish(domainEvent.GetType().Name, JsonConvert.SerializeObject(domainEvent));
                }
            }
        }

        public static PropertyBuilder<TimeSpan> IsTimeSpan(this PropertyBuilder<TimeSpan> builder)
        {
            ValueConverter<TimeSpan, double> converter = new ValueConverter<TimeSpan, double>(
                v => v.TotalSeconds,
                v => TimeSpan.FromSeconds(v));

            //ValueComparer<TimeSpan> comparer = new ValueComparer<TimeSpan>(
            //    (l, r) => JsonConvert.SerializeObject(l) == JsonConvert.SerializeObject(r),
            //    v => v == null ? 0 : JsonConvert.SerializeObject(v).GetHashCode(),
            //    v => JsonConvert.DeserializeObject<TProperty>(JsonConvert.SerializeObject(v)));

            builder.HasConversion(converter);
            //builder.Metadata.SetValueConverter(converter);
            //builder.Metadata.SetValueComparer(comparer);

            return builder;
        }
    }
}
