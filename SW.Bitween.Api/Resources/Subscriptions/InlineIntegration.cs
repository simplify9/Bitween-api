using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Subscriptions
{
    /// <summary>
    /// Turns an integration defined inline — while a gateway route or attachment is being made —
    /// into a subscription, ready to be saved alongside whatever points at it.
    /// <para>
    /// Nothing here duplicates <see cref="Create"/>: the pipeline is applied by the very same
    /// <see cref="SubscriptionConfigurationApplier"/>, and the caller does the single
    /// <c>SaveChangesAsync</c>, so the integration and its link either both exist or neither does.
    /// </para>
    /// </summary>
    public static class InlineIntegration
    {
        /// <summary>
        /// Which integration a gateway link points at. Exactly one of an existing id and an
        /// inline definition has to be given — both, or neither, is a mistake worth naming.
        /// </summary>
        public static void EnsureExactlyOne(int? subscriptionId, InlineIntegrationCreate inline)
        {
            if (subscriptionId.HasValue && inline != null)
                throw new SWValidationException(GatewayLinkTarget.BothGiven,
                    "Give either an existing integration or a new one to create, not both.");

            if (!subscriptionId.HasValue && inline == null)
                throw new SWValidationException(GatewayLinkTarget.NeitherGiven,
                    "Pick the integration this runs, or define a new one.");
        }


        /// <summary>
        /// The rules an ordinary create enforces through FluentValidation, applied to an integration
        /// arriving this way. Without them this door was a hole in the same validation: a response
        /// message name with a space in it went straight to a RabbitMQ routing key nothing can
        /// answer. Each check calls the one implementation the create handler calls.
        /// </summary>
        private static async Task CheckConfiguration(
            BitweenDbContext dbContext,
            AdapterRequirements adapterRequirements,
            InlineIntegrationCreate model)
        {
            if (!string.IsNullOrEmpty(model.ResponseMessageTypeName)
                && Regex.IsMatch(model.ResponseMessageTypeName, @"\s"))
                throw new SWValidationException("INVALID_BUS_TYPE_NAME",
                    "A bus message name cannot contain spaces.");

            var responseFailure = await ResponseRoutingValidation.CheckDestination(
                dbContext, model.ResponseSubscriptionId);
            if (responseFailure != null)
                throw new SWValidationException(
                    ResponseRoutingValidation.BusGatewayCode, responseFailure);

            // Neither gateway type carries its own partner — a partner reaches them through the
            // attachment or the route, which is the very thing being made.
            if (model.PartnerId.HasValue)
                throw new SWValidationException("PARTNER_NOT_ALLOWED",
                    "A gateway integration does not carry its own partner.");

            // A named adapter has to be usable. Naming none is still fine; a half-configured one
            // is exactly what committing here would make permanent.
            foreach (var (kind, adapterId, provided) in new[]
                     {
                         ("receiver", model.ReceiverId, model.ReceiverProperties),
                         ("validator", model.ValidatorId, model.ValidatorProperties),
                         ("mapper", model.MapperId, model.MapperProperties),
                         ("handler", model.HandlerId, model.HandlerProperties),
                     })
            {
                var missing = await adapterRequirements.MissingFor(adapterId, provided);
                if (missing.Count > 0)
                    throw new SWValidationException("ADAPTER_INCOMPLETE",
                        $"The {kind} is missing {string.Join(", ", missing)}.");
            }
        }

        /// <summary>
        /// Builds the subscription and adds it to the change tracker, so its id is available to
        /// the link being created in the same transaction. Does not save.
        /// </summary>
        public static async Task<Subscription> Stage(
            BitweenDbContext dbContext,
            AdapterRequirements adapterRequirements,
            InlineIntegrationCreate model,
            int documentId,
            SubscriptionType type)
        {
            if (string.IsNullOrWhiteSpace(model.Name))
                throw new SWValidationException("INVALID_NAME", "Give the integration a name.");

            await CheckConfiguration(dbContext, adapterRequirements, model);

            // Who chooses the information type differs by gateway kind, so the caller passes it:
            // a bus gateway is bound to one and imposes it, an API gateway is not and the caller
            // picks. Either way it is settled before Apply runs.
            if (!await dbContext.Set<Document>().AnyAsync(d => d.Id == documentId))
                throw new SWValidationException("INVALID_DOCUMENT",
                    "Choose the information type this integration carries.");

            model.DocumentId = documentId;

            var entity = new Subscription(model.Name, documentId, type);
            var trail = new SubscriptionTrail(SubscriptionTrialCode.Created, entity, true);
            dbContext.Add(trail);

            // The same code an ordinary create runs, so a field cannot work through one door
            // and not the other.
            await SubscriptionConfigurationApplier.Apply(dbContext, entity, model);

            // Neither gateway type runs on its own — a GatewayApiCall waits for an attachment, a
            // BusGateway for a route — and the one being made is in this same transaction.
            entity.Inactive = false;

            dbContext.Add(entity);
            return entity;
        }
    }
}
