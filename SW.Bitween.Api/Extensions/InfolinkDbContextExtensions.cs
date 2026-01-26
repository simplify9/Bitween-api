using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Bitween
{
    static class BitweenDbContextExtensions
    {
        public static async Task<(Partner Partner, string KeyName)> AuthorizePartner(this BitweenDbContext dbContext,
            RequestContext requestContext)
        {
            var partnerKey = requestContext.Values.Where(item => item.Name.ToLower() == "partnerkey")
                .Select(item => item.Value).FirstOrDefault();
            if (partnerKey == null)
                throw new SWUnauthorizedException();

            var partnerQuery = from partner in dbContext.Set<Partner>()
                where partner.ApiCredentials.Any(cred => cred.Key == partnerKey)
                select partner;

            var par = await partnerQuery.AsNoTracking().SingleOrDefaultAsync();
            if (par == null)
                throw new SWUnauthorizedException();

            return (par, par.ApiCredentials.First(c => c.Key == partnerKey).Name);
        }

        public static IQueryable<Subscription> Subscriptions(this BitweenDbContext dbContext) =>
            dbContext.Set<Subscription>().Include(s => s.WorkGroup);
    }
    
}