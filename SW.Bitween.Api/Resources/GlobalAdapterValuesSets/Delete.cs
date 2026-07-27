using System.Threading.Tasks;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.GlobalAdapterValuesSets
{
    [HandlerName("delete")]
    public class Delete : ICommandHandler<string, DeleteGlobalAdapterValuesSetModel, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public Delete(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(string key, DeleteGlobalAdapterValuesSetModel _)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.GlobalValues.Delete);

            var entity = await _dbContext.Set<GlobalAdapterValuesSet>().FindAsync(key);
            if (entity is null)
                throw new SWValidationException("NOT_FOUND", $"GlobalAdapterValuesSet with id {key} was not found");

            _dbContext.Remove(entity);
            await _dbContext.SaveChangesAsync();
            return null;
        }
    }
}
