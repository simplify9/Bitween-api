using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using SW.EfCoreExtensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using SW.Infolink.Model;

namespace SW.Infolink.Api.Resources.Xchanges
{
    [HandlerName("retry")]
    class Retry : ICommandHandler<int, XchangeRequest>
    {
        private readonly InfolinkDbContext dbContext;
        public XchangeService XchangeService { get; }
        public Retry(InfolinkDbContext dbContext, XchangeService xchangeService)
        {
            this.dbContext = dbContext;
            XchangeService = xchangeService;
        }



        public async Task<object> Handle(int key, XchangeRequest request)
        {

            await XchangeService.Retry(key);
            return null;
        }
    }
}
