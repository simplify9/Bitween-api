using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Notifiers
{
    public class Delete : IDeleteHandler<int,object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;
        private readonly IInfolinkCache _cache;

        public Delete(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
            _cache = cache;
        }

        /// <remarks>
        /// No reference check, unlike an integration's delete: nothing has a foreign key to a
        /// notifier. <c>RunOnSubscriptions</c> points the other way — the notifier names the
        /// integrations it watches, so deleting it takes the whole list with it.
        /// </remarks>
        public async Task<object> Handle(int key)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Notifiers.Delete);

            await _dbContext.DeleteByKeyAsync<Notifier>(key);
            await _cache.BroadcastRevoke();
            return null;
        }
    }
}
