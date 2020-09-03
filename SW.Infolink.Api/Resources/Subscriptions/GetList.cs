using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Subscribers
{
    [HandlerName("getlist")]
    class GetList : IQueryHandler
    {
        private readonly InfolinkDbContext dbContext;

        public GetList(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }
        public async Task<object> Handle()
        {
            return await dbContext.Set<Subscription>().
                Select(document => new SubscriptionRow
                {
                    Id=document.Id, 
                    Name = document.Name,

                }).ToListAsync();
        }
    }
}
