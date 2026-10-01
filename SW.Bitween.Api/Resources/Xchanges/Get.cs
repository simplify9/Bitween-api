using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Xchanges
{
    [Unprotect]
public class Get(BitweenDbContext dbContext, RequestContext requestContext, XchangeService xchangeService,
    BitweenOptions options)
        : IGetHandler<string,object>
    {
        private readonly BitweenDbContext dbContext = dbContext;
        private readonly RequestContext requestContext = requestContext;
        private readonly XchangeService xchangeService = xchangeService;

        async public Task<object> Handle(string key)//, bool lookup = false)
        {
            var par = await dbContext.AuthorizePartner(requestContext, options.PartnerKeyHeader);

            if (par.Partner.Id == Partner.SystemId)
            {
                return null;
            }

            var query = from xchange in dbContext.Set<Xchange>()
                        join result in dbContext.Set<XchangeResult>() on xchange.Id equals result.Id into xr
                        from result in xr.DefaultIfEmpty()
                        join subscriber in dbContext.Set<Subscription>() on xchange.SubscriptionId equals subscriber.Id
                        where xchange.Id == key && subscriber.PartnerId == par.Partner.Id
                        select new { xchange, result };

            var queryResult = await query.AsNoTracking().SingleOrDefaultAsync();

            if (queryResult == null)
                throw new SWNotFoundException(key);

            else if (queryResult.result == null || !queryResult.result.Success)
                return new XchangeGetResultResponse();

            else
                return new XchangeGetResultResponse
                {
                    Success = true,
                    //InputUri = xchangeService.GetFileUrl(queryResult.xchange.Id, XchangeFileType.Input),
                    //OutputUri = xchangeService.GetFileUrl(queryResult.xchange.Id, XchangeFileType.Output),
                    // A sealed Bitween link: the partner follows it with no login, as it did the public
                    // storage URL this used to be, and exchange files are private in storage now.
                    ResponseUri = xchangeService.FileUrl(queryResult.xchange.Id, queryResult.xchange.FilesPrefix, XchangeFileType.Response),
                };
        }

        
    }

}

