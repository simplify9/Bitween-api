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
using SW.Infolink.Domain.Accounts;

namespace SW.Infolink.Api.Resources.Documents
{
    class Create : ICommandHandler<DocumentCreate>
    {
        private readonly InfolinkDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public Create(InfolinkDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(DocumentCreate model)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

            var entity = new Document(model.Id, model.Name, model.DocumentFormat);
            var trail = new DocumentTrail(DocumentTrailCode.Created, entity, true);
            _dbContext.Add(trail);
            _dbContext.Add(entity);
            await _dbContext.SaveChangesAsync();
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