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
    public class Create(BitweenDbContext dbContext, RequestContext requestContext, IBroadcast broadcast,
        IInfolinkCache cache) : ICommandHandler<DocumentCreate,object>
    {
        public async Task<object> Handle(DocumentCreate model)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Documents.Create);

            // Same check Update makes, and compared the same way. Without it two types
            // could be created under one name, and then neither could be saved again —
            // Update refuses the name it already has. Ignoring case, because a list
            // holding both "Invoice" and "invoice" reads as a mistake, not a choice.
            var wantedName = (model.Name ?? string.Empty).ToLower();
            if (await dbContext.Set<Document>().AsNoTracking().AnyAsync(d => d.Name.ToLower() == wantedName))
                throw new SWValidationException("NAME_TAKEN", "An information type with this name already exists.");

            var code = string.IsNullOrWhiteSpace(model.Code) ? null : model.Code;

            if (code != null && await dbContext.Set<Document>().AsNoTracking().AnyAsync(d => d.Code == code))
                throw new SWValidationException("CODE_TAKEN", "This code is already in use.");

            // Matching exactly here once let "Foo" and "foo" both exist, and then every message
            // published under either name reached both gateways, silently.
            if (model.BusEnabled)
                await BusMessageTypeNames.EnsureFree(dbContext, model.BusMessageTypeName);

            PromotedPropertyValidation.Check(model.PromotedProperties, model.DocumentFormat);
            await DocumentSchema.EnsureUsable(model.DocumentFormat, model.ValidationSchema);

            var entity = new Document(code, model.Name, model.DocumentFormat)
            {
                BusEnabled = model.BusEnabled,
                BusMessageTypeName = model.BusEnabled ? model.BusMessageTypeName : null,
                DuplicateInterval = model.DuplicateInterval,
                DisregardsUnfilteredMessages = model.DisregardsUnfilteredMessages,
            };
            if (model.PromotedProperties != null)
                entity.SetDictionaries(model.PromotedProperties.ToDictionary());

            entity.SetValidationSchema(model.ValidationSchema);
            dbContext.Add(entity);
            await dbContext.SaveChangesAsync();
            // Routing resolves an information type by name off the cache, so a new one is
            // invisible to it until this lands.
            await cache.BroadcastRevoke();

            // A bus-enabled type adds a queue, and the consumer set is only rebuilt when asked.
            // Without this the queue is declared but nothing ever consumes it, until either an
            // unrelated document update happens to refresh consumers or the app restarts.
            if (entity.BusEnabled)
                await broadcast.RefreshConsumers();

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