using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Resources.Inbox
{
    class Get : IQueryHandler<string, object>
    {
        private readonly InfolinkDbContext dbContext;

        public Get(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        async public Task<object> Handle(string documentIdOrName, object request)
        {
            Document document = null;


            if (int.TryParse(documentIdOrName, out var documentId))
                document = await dbContext.FindAsync<Document>(documentId);
            else
                document = await dbContext.Set<Document>().Where(doc => doc.Name == documentIdOrName).SingleOrDefaultAsync();

            if (document == null)
                throw new SWNotFoundException("Document");

            return null;
        }
    }
}
