using FluentValidation;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Api.Resources.Documents
{
    class Create : ICommandHandler<DocumentCreate>
    {
        private readonly InfolinkDbContext dbContext;

        public Create(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }

        public async Task<object> Handle(DocumentCreate model)
        {
            var entity = new Document(model.Id, model.Name, model.DocumentFormat);
            var trail = new DocumentTrail(DocumentTrailCode.Created, entity);
            dbContext.Add(trail);
            dbContext.Add(entity);
            await dbContext.SaveChangesAsync();
            return entity.Id;
        }

        private class Validate : AbstractValidator<DocumentCreate>
        {
            public Validate()
            {
                RuleFor(i => i.Id).NotEmpty();
                RuleFor(i => i.Name).NotEmpty();
            }
        }
    }
}