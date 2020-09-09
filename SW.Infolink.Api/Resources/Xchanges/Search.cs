using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using SW.EfCoreExtensions;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.Infolink.Model;

namespace SW.Infolink.Resources.Xchanges
{
    class Search : ISearchyHandler
    {
        private readonly InfolinkDbContext dbContext;
        private readonly XchangeService xchangeService;

        public Search(InfolinkDbContext dbContext, XchangeService xchangeService)
        {
            this.dbContext = dbContext;
            this.xchangeService = xchangeService;
        }

        async public Task<object> Handle(SearchyRequest searchyRequest, bool lookup = false, string searchPhrase = null)
        {
            var query = from xchange in dbContext.Set<Xchange>()
                        join result in dbContext.Set<XchangeResult>() on xchange.Id equals result.Id into xr
                        from result in xr.DefaultIfEmpty()
                        join delivery in dbContext.Set<XchangeDelivery>() on xchange.Id equals delivery.Id into xd
                        from delivery in xd.DefaultIfEmpty()
                        join agg in dbContext.Set<XchangeAggregation>() on xchange.Id equals agg.Id into xa
                        from agg in xa.DefaultIfEmpty()
                        join document in dbContext.Set<Document>() on xchange.DocumentId equals document.Id
                        join subscriber in dbContext.Set<Subscription>() on xchange.SubscriptionId equals subscriber.Id into xs
                        from subscriber in xs.DefaultIfEmpty()
                        select new XchangeRow
                        {
                            Id = xchange.Id,
                            HandlerId = xchange.HandlerId,
                            MapperId = xchange.MapperId,
                            DocumentId = xchange.DocumentId,
                            DocumentName = document.Name,
                            StartedOn = xchange.StartedOn,
                            FinishedOn = result.FinishedOn,
                            SubscriptionId = xchange.SubscriptionId,
                            SubscriptionName = subscriber.Name,
                            Status = result.Success,
                            InputUrl = xchangeService.GetFileUrl(xchange.Id, XchangeFileType.Input),
                            OutputUrl = xchangeService.GetFileUrl(xchange.Id, XchangeFileType.Output),
                            ResponseUrl = xchangeService.GetFileUrl(xchange.Id, XchangeFileType.Response),
                            Duration = xchange.StartedOn.Elapsed(result.FinishedOn)
                            //Exception = xchange.Exception
                        };

            var searchyResponse = new SearchyResponse<XchangeRow>
            {
                Result = await query.OrderByDescending(p => p.StartedOn).AsNoTracking().Search(searchyRequest.Conditions, searchyRequest.Sorts, searchyRequest.PageSize, searchyRequest.PageIndex).ToListAsync(),
                TotalCount = await query.AsNoTracking().Search(searchyRequest.Conditions).CountAsync()
            };

            return searchyResponse;
        }


    }
}
