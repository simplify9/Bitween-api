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

namespace SW.Infolink.Resources.Xchanges
{
    [HandlerName("retry")]
    class Retry : ICommandHandler<object>
    {
        private readonly InfolinkDbContext dbContext;
        private readonly XchangeService xchangeService;

        public Retry(InfolinkDbContext dbContext, XchangeService xchangeService)
        {
            this.dbContext = dbContext;
            this.xchangeService = xchangeService;
        }



        public async Task<object> Handle(object obj)
        {
            
            //await XchangeService.Retry(key);
            return null;
        }
    }
}
