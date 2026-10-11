using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Gateway;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Documents
{
    /// <summary>
    /// Takes an information type out of use, or puts it back. Toggles, the same way a
    /// subscription's pause does.
    /// </summary>
    /// <remarks>
    /// The gentle half of <see cref="Delete"/>: a type that has carried traffic usually wants to
    /// leave the pickers without taking its exchanges with it, and this is reversible where a
    /// delete is not.
    /// </remarks>
    [HandlerName("retire")]
    public class Retire(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        : ICommandHandler<int, DocumentRetire, object>
    {
        public async Task<object> Handle(int key, DocumentRetire request)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Documents.Edit);

            if (key == Document.AggregationDocumentId)
                throw new SWException("The built-in Aggregation Document can't be retired.");

            var entity = await dbContext.FindAsync<Document>(key);
            if (entity == null)
                throw new SWNotFoundException("Information type not found.");

            if (entity.RetiredOn == null)
            {
                // Configuration still pointing at it would be left running against a type the UI
                // no longer offers - which reads as the type having been removed when it has not.
                // Said before retiring rather than after, so nothing half-happens.
                if (await dbContext.Set<Subscription>().AnyAsync(s => s.DocumentId == key))
                    throw new SWException(
                        "Cannot retire an information type that subscriptions still carry. Delete them, or point them at another type, first.");

                if (await dbContext.Set<BusGateway>().AnyAsync(g => g.DocumentId == key))
                    throw new SWException(
                        "Cannot retire an information type that a bus gateway still listens for. Delete the gateway, or point it at another type, first.");

                entity.Retire();
            }
            else
            {
                entity.Restore();
            }

            await dbContext.SaveChangesAsync();
            // The receiving path resolves types through the cache, so a retired type would keep
            // being offered - and keep accepting work - for the rest of the cache's window.
            await cache.BroadcastRevoke();

            return new { entity.Id, entity.RetiredOn };
        }
    }
}
