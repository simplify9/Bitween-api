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

namespace SW.Bitween.Resources.Documents
{
    public class Create : ICommandHandler<DocumentCreate,object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;
        private readonly IBroadcast _broadcast;

        public Create(BitweenDbContext dbContext, RequestContext requestContext, IBroadcast broadcast)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
            _broadcast = broadcast;
        }

        public async Task<object> Handle(DocumentCreate model)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Documents.Create);

            // Same check Update makes, and compared the same way. Without it two types
            // could be created under one name, and then neither could be saved again —
            // Update refuses the name it already has. Ignoring case, because a list
            // holding both "Invoice" and "invoice" reads as a mistake, not a choice.
            var wantedName = (model.Name ?? string.Empty).ToLower();
            if (await _dbContext.Set<Document>().AsNoTracking().AnyAsync(d => d.Name.ToLower() == wantedName))
                throw new SWValidationException("NAME_TAKEN", "An information type with this name already exists.");

            var code = string.IsNullOrWhiteSpace(model.Code) ? null : model.Code;

            if (code != null && await _dbContext.Set<Document>().AsNoTracking().AnyAsync(d => d.Code == code))
                throw new SWValidationException("CODE_TAKEN", "This code is already in use.");

            if (model.BusEnabled && !string.IsNullOrEmpty(model.BusMessageTypeName))
            {
                // Compared lower-cased, because that is how the bus compares them: both
                // BasicPublisher and ConsumerDefinition derive the routing key with
                // ToLower(), so "Foo" and "foo" are one message on the wire. Matching
                // exactly here let both exist, and then every message published under
                // either name reached both gateways, silently. ToLower() rather than a
                // provider-specific collation — this runs on Postgres, MySql and MsSql.
                var wanted = model.BusMessageTypeName.ToLower();
                var busTypeNameDuplicated = await _dbContext.Set<Document>()
                    .AsNoTracking()
                    .AnyAsync(d => d.BusMessageTypeName.ToLower() == wanted);
                if (busTypeNameDuplicated)
                    throw new SWValidationException("DUPLICATED_BUS_TYPE_NAME",
                        $"Another information type already publishes as '{model.BusMessageTypeName}'. " +
                        "Names are compared ignoring case, because the bus does.");
            }

            PromotedPropertyValidation.Check(model.PromotedProperties, model.DocumentFormat);

            var entity = new Document(code, model.Name, model.DocumentFormat)
            {
                BusEnabled = model.BusEnabled,
                BusMessageTypeName = model.BusEnabled ? model.BusMessageTypeName : null,
                DuplicateInterval = model.DuplicateInterval,
                DisregardsUnfilteredMessages = model.DisregardsUnfilteredMessages,
            };
            if (model.PromotedProperties != null)
                entity.SetDictionaries(model.PromotedProperties.ToDictionary());

            // After the entity is complete: the trail serialises it in the constructor
            // when isNew, so anything set later would be missing from the created state.
            var trail = new DocumentTrail(DocumentTrailCode.Created, entity, true);
            _dbContext.Add(trail);
            _dbContext.Add(entity);
            await _dbContext.SaveChangesAsync();

            // A bus-enabled type adds a queue, and the consumer set is only rebuilt when asked.
            // Without this the queue is declared but nothing ever consumes it, until either an
            // unrelated document update happens to refresh consumers or the app restarts.
            if (entity.BusEnabled)
                await _broadcast.RefreshConsumers();

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
                RuleFor(i => i.BusMessageTypeName)
                    .Matches("^\\S+$")
                    .When(i => !string.IsNullOrEmpty(i.BusMessageTypeName))
                    .WithMessage("Bus message type name cannot contain spaces.");
            }
        }
    }
}