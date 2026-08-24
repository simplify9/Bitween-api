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

namespace SW.Bitween.Resources.Documents
{
    public class Get : IGetHandler<int,object>
    {
        private readonly BitweenDbContext dbContext;
        private readonly RequestContext requestContext;

        public Get(BitweenDbContext dbContext, RequestContext requestContext)
        {
            this.dbContext = dbContext;
            this.requestContext = requestContext;
        }

        public async Task<object> Handle(int key)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Documents.View);

            return await dbContext.Set<Document>().Search("Id", key).Select(document => new DocumentUpdate
            {
                Id = document.Id,
                Code = document.Code,
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