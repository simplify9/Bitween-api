using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Partners
{
    [HandlerName("withexternalaccountcode")]
    public class GetWithExternalAccountCode : IGetHandler<string, object>
    {
        private readonly BitweenDbContext _dbContext;

        public GetWithExternalAccountCode(BitweenDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<object> Handle(string key)
        {
            var allPartners = await _dbContext.Set<Partner>()
                .AsNoTracking()
                .Where(p => p.AdapterProperties != null)
                .Select(p => new { p.Id, p.AdapterProperties })
                .ToListAsync();

            var partners = allPartners
                .Where(p => p.AdapterProperties.ContainsKey("ExternalAccountCode"))
                .Select(p => new
                {
                    id = p.Id,
                    externalAccountCode = p.AdapterProperties["ExternalAccountCode"],
                    externalAccountCodePath = p.AdapterProperties.ContainsKey("ExternalAccountCodePath")
                        ? p.AdapterProperties["ExternalAccountCodePath"]
                        : (string)null
                })
                .ToList();

            return partners;
        }
    }
}
