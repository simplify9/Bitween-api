using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Resources.Ops;

namespace SW.Bitween.Resources.Documents
{
public class Delete(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache,
        IBroadcast broadcast, BrokerQueues brokerQueues)
        : IDeleteHandler<int,object>
    {
        async public Task<object> Handle(int key)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Documents.Delete);

            // Seeded by the model and referenced by every aggregation, so it is not the user's
            // to remove — the same footing as the system partner.
            if (key == Document.AggregationDocumentId)
                throw new SWException("The built-in Aggregation Document can't be deleted.");

            // Configuration pointing at this type is a real block — a subscription or a bus
            // gateway left behind would name a type that no longer exists. Said plainly, because
            // the database says it as a foreign-key violation, which reached the screen as a bare
            // 500 naming a constraint.
            if (await dbContext.Set<Subscription>().AnyAsync(s => s.DocumentId == key))
                throw new SWException(
                    "Cannot delete an information type that subscriptions still carry. Delete them, or point them at another type, first.");

            if (await dbContext.Set<BusGateway>().AnyAsync(g => g.DocumentId == key))
                throw new SWException(
                    "Cannot delete an information type that a bus gateway still listens for. Delete the gateway, or point it at another type, first.");

            var busMessageTypeName = await dbContext.Set<Document>().Where(d => d.Id == key)
                .Select(d => d.BusMessageTypeName).FirstOrDefaultAsync();

            // Exchanges are the deliberate exception. They are this type's history rather than
            // configuration depending on it, and history should not be able to strand a type
            // nobody uses any more — so they go with it.
            await using var transaction = await dbContext.Database.BeginTransactionAsync();

            var xchangeIds = dbContext.Set<Xchange>().Where(x => x.DocumentId == key).Select(x => x.Id);

            // Scheduled retries are keyed by exchange id but have no relationship configured, so
            // nothing clears them on their own — they would be left pointing at exchanges that no
            // longer exist. Results, aggregations and promoted properties all cascade from the
            // exchange itself and need no help here.
            await dbContext.Set<DelayedRetry>().Where(d => xchangeIds.Contains(d.Id)).ExecuteDeleteAsync();
            await dbContext.Set<Xchange>().Where(x => x.DocumentId == key).ExecuteDeleteAsync();

            await dbContext.DeleteByKeyAsync<Document>(key);

            await transaction.CommitAsync();
            await cache.BroadcastRevoke();
            // Without this the bus kept consuming a type that no longer exists until a restart.
            await broadcast.RefreshConsumers();
            // A paused type still has its name, and its queue: that goes too.
            if (!string.IsNullOrWhiteSpace(busMessageTypeName))
                await brokerQueues.DeleteInformationTypeLane(busMessageTypeName);
            return null;
        }
    }
}
