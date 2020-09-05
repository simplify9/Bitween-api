using Microsoft.EntityFrameworkCore.ChangeTracking;
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
    }
}
