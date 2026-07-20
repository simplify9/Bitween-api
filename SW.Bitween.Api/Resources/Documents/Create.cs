using FluentValidation;
using Microsoft.EntityFrameworkCore;
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

namespace SW.Bitween.Resources.Documents
{
    public class Create : ICommandHandler<DocumentCreate,object>
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

            var code = string.IsNullOrWhiteSpace(model.Code) ? null : model.Code;

            if (code != null && await _dbContext.Set<Document>().AsNoTracking().AnyAsync(d => d.Code == code))
                throw new SWValidationException("CODE_TAKEN", "This code is already in use.");

            if (model.BusEnabled && !string.IsNullOrEmpty(model.BusMessageTypeName))
            {
                var busTypeNameDuplicated = await _dbContext.Set<Document>()
                    .AsNoTracking()
                    .AnyAsync(d => d.BusMessageTypeName == model.BusMessageTypeName);
                if (busTypeNameDuplicated)
                    throw new SWValidationException("DUPLICATED_BUS_TYPE_NAME",
                        "Cant use duplicated bus Message type name");
            }

            var entity = new Document(code, model.Name, model.DocumentFormat)
            {
                BusEnabled = model.BusEnabled,
                BusMessageTypeName = model.BusEnabled ? model.BusMessageTypeName : null,
            };
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
                RuleFor(i => i.Code)
                    .Matches("^[A-Z][A-Z0-9_]{1,49}$")
                    .When(i => !string.IsNullOrEmpty(i.Code))
                    .WithMessage("Codes are upper-case letters, digits and underscores (2-50 chars).");
                RuleFor(i => i.Name).NotEmpty();
            }
        }
    }
}