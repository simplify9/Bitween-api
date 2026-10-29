using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Notifiers
{
public class Delete(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        : IDeleteHandler<int,object>
    {
        /// <remarks>
        /// No reference check, unlike an integration's delete: nothing has a foreign key to a
        /// notifier. <c>RunOnSubscriptions</c> points the other way - the notifier names the
        /// integrations it watches, so deleting it takes the whole list with it.
        /// </remarks>
        public async Task<object> Handle(int key)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Notifiers.Delete);

            await dbContext.DeleteByKeyAsync<Notifier>(key);
            await cache.BroadcastRevoke();
            return null;
        }
    }
}
