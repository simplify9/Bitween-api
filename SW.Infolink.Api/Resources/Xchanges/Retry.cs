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
    class Retry : ICommandHandler<string, XchangeRetry>
    {
        private readonly InfolinkDbContext dbContext;
        private readonly XchangeService xchangeService;

        public Retry(InfolinkDbContext dbContext, XchangeService xchangeService)
        {
            this.dbContext = dbContext;
            this.xchangeService = xchangeService;
        }

        public async Task<object> Handle(string key, XchangeRetry xchangeRetry)
        {
            var xchange = await dbContext.FindAsync<Xchange>(key);
            var inputFileData = await xchangeService.GetFile(xchange.Id, XchangeFileType.Input);
            var xchangeFile = new XchangeFile(inputFileData, xchange.InputName);
            await xchangeService.CreateXchange(xchange, xchangeFile);
            await dbContext.SaveChangesAsync();
            
            return null;
        }
    }
}
