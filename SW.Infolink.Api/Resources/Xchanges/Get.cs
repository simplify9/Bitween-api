using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using SW.EfCoreExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SW.Infolink.Model;

namespace SW.Infolink.Api.Resources.Xchanges
{
    class Get : IGetHandler<int>
    {
        private readonly InfolinkDbContext dbContext;

        public Get(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(int key, bool lookup = false)
        {
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
                    SubscriberId = xchange.SubscriptionId,
                    //Status = xchange.Status,
                    //Exception = xchange.Exception,
                    InputFileName = xchange.InputFileName  

                }).SingleOrDefaultAsync();
        }
    }
}
