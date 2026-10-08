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
    public class Get(BitweenDbContext dbContext, RequestContext requestContext) : IGetHandler<int,object>
    {
        private readonly BitweenDbContext dbContext = dbContext;
        private readonly RequestContext requestContext = requestContext;

        public async Task<object> Handle(int key)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Documents.View);

            // DocumentRow rather than DocumentUpdate: it is the read shape, and RetiredOn belongs
            // on it rather than on the model used to write a type — retiring is its own command,
            // not a field an update can set.
            return await dbContext.Set<Document>().Search("Id", key).Select(document => new DocumentRow
            {
                Id = document.Id,
                Code = document.Code,
                Name = document.Name,
                BusEnabled = document.BusEnabled,
                BusMessageTypeName = document.BusMessageTypeName,
                DuplicateInterval = document.DuplicateInterval,
                PromotedProperties = document.PromotedProperties.ToKeyAndValueCollection(),
                ValidationSchema = document.ValidationSchema,
                DocumentFormat = document.DocumentFormat,
                DisregardsUnfilteredMessages = document.DisregardsUnfilteredMessages ?? false,
                RetiredOn = document.RetiredOn,
                UsedByCount = dbContext.Set<Subscription>()
                    .Count(subscription => subscription.DocumentId == document.Id)
            }).SingleOrDefaultAsync();
        }
    }
}