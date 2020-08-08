using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Xchanges
{
    class Create : ICommandHandler<XchangeRequest>
    {
        private readonly InfolinkDbContext dbContext;
        private readonly XchangeService xchangeService;
        private readonly FilterService filterService;

        public Create(InfolinkDbContext dbContext, XchangeService xchangeService, FilterService filterService)
        {
            this.dbContext = dbContext;
            this.xchangeService = xchangeService;
            this.filterService = filterService;
        }

        async public Task<object> Handle(XchangeRequest request)
        {
            if (request.SubscriberId > 0)
            {
                var id = await xchangeService.Submit(request.SubscriberId,
                    request.File,
                    request.References,
                    request.IgnoreSchedule);

                return id.ToString();//CreatedAtAction(nameof(GetTodoItem), new { id = item.Id }, item);

            }
            else if (request.DocumentId > 0)
            {


                var subscribers = filterService.Filter(request.DocumentId, request.File);

                foreach (var sub in subscribers)
                {
                    await xchangeService.Submit(sub, request.File);
                }

                return null;

            }
            else
            {
                throw new ArgumentException();
            }
        }
    }
}
