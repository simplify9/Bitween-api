using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Xchanges
{
    class Create : ICommandHandler<XchangeRequest>
    {
        private readonly XchangeService xchangeService;
        private readonly FilterService filterService;

        public Create(XchangeService xchangeService, FilterService filterService)
        {
            this.xchangeService = xchangeService;
            this.filterService = filterService;
        }

        async public Task<object> Handle(XchangeRequest request)
        {
            if (request.SubscriberId > 0)
            {
                var id = await xchangeService.Submit(request.SubscriberId,
                    new XchangeFile(request.Data, request.Filename),
                    request.References,
                    false);

                return id.ToString();

            }
            else if (request.DocumentId > 0)
            {
                var subscribers = filterService.Filter(request.DocumentId, new XchangeFile(request.Data, request.Filename));

                foreach (var sub in subscribers)
                {
                    await xchangeService.Submit(sub, new XchangeFile(request.Data, request.Filename));
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
