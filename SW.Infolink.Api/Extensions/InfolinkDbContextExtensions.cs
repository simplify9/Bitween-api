using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink
{
    static class InfolinkDbContextExtensions
    {
        async static public Task<Partner> AuthorizePartner(this InfolinkDbContext dbContext, RequestContext requestContext )
        {
            var partnerKey = requestContext.Values.Where(item => item.Name.ToLower() == "partnerkey").Select(item => item.Value).FirstOrDefault();
            if (partnerKey == null)
                throw new SWUnauthorizedException();

            var partnerQuery = from partner in dbContext.Set<Partner>()
                               where partner.ApiCredentials.Any(cred => cred.Key == partnerKey)
                               select partner;

            var par = await partnerQuery.AsNoTracking().SingleOrDefaultAsync();
            if (par == null)
                throw new SWUnauthorizedException();

            return par;
        }
    }
}
