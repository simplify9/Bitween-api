using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Infolink.Resources.Xchanges
{
    [Unprotect]
    class Get : IGetHandler<string>
    {
        private readonly InfolinkDbContext dbContext;
        private readonly RequestContext requestContext;
        private readonly XchangeService xchangeService;

        public Get(InfolinkDbContext dbContext, RequestContext requestContext, XchangeService xchangeService)
        {
            this.dbContext = dbContext;
            this.requestContext = requestContext;
            this.xchangeService = xchangeService;
        }

        async public Task<object> Handle(string key, bool lookup = false)
        {
            var par = await dbContext.AuthorizePartner(requestContext);

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
                    ResponseUri = xchangeService.GetFileUrl(queryResult.xchange.Id, XchangeFileType.Response),
                };
        }
    }

}

