using FluentValidation;
using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Api.Resources.Documents
{
    class Create : ICommandHandler<DocumentCreate,object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public Create(BitweenDbContext dbContext, RequestContext requestContext)
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