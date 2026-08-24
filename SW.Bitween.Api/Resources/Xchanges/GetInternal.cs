using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.PrimitiveTypes;
using SW.EfCoreExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SW.Bitween.Model;

namespace SW.Bitween.Resources.Xchanges
{
    [HandlerName("internal")]
    public class GetInternal : IGetHandler<int,object>
    {
        private readonly BitweenDbContext dbContext;
        private readonly RequestContext requestContext;

        public GetInternal(BitweenDbContext dbContext, RequestContext requestContext)
        {
            this.dbContext = dbContext;
            this.requestContext = requestContext;
        }

        async public Task<object> Handle(int key)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Exchanges.View);

            return await dbContext.Set<Xchange>().AsNoTracking().
                Search("Id", key).
                Select( xchange => new XchangeRow
                {
                    Id = xchange.Id,
                    HandlerId = xchange.HandlerId,
                    MapperId = xchange.MapperId,
                    DocumentId = xchange.DocumentId,
                    StartedOn = xchange.StartedOn,
                    //FinishedOn = xchange.FinishedOn,
                    SubscriptionId = xchange.SubscriptionId,
                    //Status = xchange.Status,
                    //Exception = xchange.Exception,
                    InputFileName = xchange.InputName  

                }).SingleOrDefaultAsync();
        }
    }
}
