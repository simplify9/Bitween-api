using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using SW.EfCoreExtensions;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Model;

namespace SW.Bitween.Resources.Partners
{
    public class Get : IGetHandler<int,object>
    {
        private readonly BitweenDbContext dbContext;

        public Get(BitweenDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(int key)
        {
            return await dbContext.Set<Partner>().AsNoTracking().
                Search("Id", key).
                Select(partner => new PartnerUpdate
                {
                    Name = partner.Name,

                    ApiCredentials = partner.ApiCredentials.Select(cred => new KeyAndValue
                    {
                        Key = cred.Name,
                        Value = $"{cred.Key.Remove(5)}...(hidden)"
                    }).ToList(),

                    Subscriptions = partner.Subscriptions.Select(sub => new SubscriptionSearch
                    {
                        Id = sub.Id,
                        Name = sub.Name,
                        Type = sub.Type,
                        DocumentId = sub.DocumentId,

                    }).ToList()

                }).AsNoTracking().SingleOrDefaultAsync();
        }
    }
}
