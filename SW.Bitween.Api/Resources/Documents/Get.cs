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

namespace SW.Bitween.Api.Resources.Documents
{
    class Get : IGetHandler<int,object>
    {
        private readonly BitweenDbContext dbContext;

        public Get(BitweenDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        public async Task<object> Handle(int key)
        {
            return await dbContext.Set<Document>().Search("Id", key).Select(document => new DocumentUpdate
            {
                Id = document.Id,
                Name = document.Name,
                BusEnabled = document.BusEnabled,
                BusMessageTypeName = document.BusMessageTypeName,
                DuplicateInterval = document.DuplicateInterval,
                PromotedProperties = document.PromotedProperties.ToKeyAndValueCollection(),
                DocumentFormat = document.DocumentFormat,
                DisregardsUnfilteredMessages = document.DisregardsUnfilteredMessages ?? false
            }).SingleOrDefaultAsync();
        }
    }
}