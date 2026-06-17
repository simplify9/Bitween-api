using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Partners
{
    [HandlerName("UpdateAdapterProperties")]
    public class UpdateAdapterProperties : ICommandHandler<int, Dictionary<string, string>, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public UpdateAdapterProperties(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(int key, Dictionary<string, string> model)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

            var entity = await _dbContext.FindAsync<Partner>(key);

            var existing = entity.AdapterProperties ?? new Dictionary<string, string>();

            var unknownKeys = model.Keys.Where(k => !existing.ContainsKey(k)).ToList();
            if (unknownKeys.Count > 0)
                throw new Exception($"The following adapter property keys do not exist: {string.Join(", ", unknownKeys)}");

            foreach (var (k, v) in model)
                existing[k] = v;

            entity.AdapterProperties = existing;
            _dbContext.Entry(entity).Property(p => p.AdapterProperties).IsModified = true;
            await _dbContext.SaveChangesAsync();
            return null;
        }
    }
}
