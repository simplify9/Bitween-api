using FluentValidation;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SW.Bitween.Resources.Subscriptions
{
    /// <summary>
    /// Creates a subscription, complete, in one transaction.
    /// <para>
    /// It used to accept only a name, a document and a type, so a client wanting a working
    /// integration had to POST and then PATCH. The POST committed on its own, so a rejected
    /// PATCH left an empty subscription behind that nobody asked for and nothing cleaned up.
    /// Everything now lands in a single <c>SaveChangesAsync</c>: either the integration exists
    /// as asked for, or it does not exist.
    /// </para>
    /// <para>
    /// The pipeline is optional. A caller sending only the original four fields still gets the
    /// empty, inactive subscription it always did.
    /// </para>
    /// </summary>
    public class Create : ICommandHandler<SubscriptionCreate, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;
        private readonly IInfolinkCache _BitweenCache;
        private readonly SubscriptionSchedulerService _subScheduler;

        public Create(BitweenDbContext dbContext, RequestContext requestContext,
            IInfolinkCache BitweenCache, SubscriptionSchedulerService subScheduler)
        {
            this._dbContext = dbContext;
            _requestContext = requestContext;
            _BitweenCache = BitweenCache;
            _subScheduler = subScheduler;
        }

        public async Task<object> Handle(SubscriptionCreate model)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Subscriptions.Create);

            Subscription entity;

            switch (model.Type)
            {
                case SubscriptionType.Receiving:
                    entity = new Subscription(model.Name, model.DocumentId);
                    break;
                case SubscriptionType.Aggregation:
                    entity = new Subscription(model.Name, model.AggregationForId!.Value, model.PartnerId!.Value);
                    break;
                case SubscriptionType.ApiCall:
                case SubscriptionType.Internal:
                    entity = new Subscription(model.Name, model.DocumentId, model.Type, model.PartnerId!.Value);
                    break;
                case SubscriptionType.GatewayApiCall:
                case SubscriptionType.BusGateway:
                    entity = new Subscription(model.Name, model.DocumentId, model.Type);
                    break;

                case SubscriptionType.Unknown:
                default:
                    throw new BitweenException();
            }

            var trail = new SubscriptionTrail(SubscriptionTrialCode.Created, entity, true);
            _dbContext.Add(trail);

            // Same code the update handler applies, so a field can't work on one and not the other.
            await SubscriptionConfigurationApplier.Apply(_dbContext, entity, model);

            // Every constructor starts it inactive. Only an explicit false turns that around, so a
            // caller that doesn't mention it keeps the behaviour it has always had.
            if (model.Inactive.HasValue) entity.Inactive = model.Inactive.Value;

            await _dbContext.SaveChangesAsync();

            // Both of these used to be the follow-up update's job. Now that a subscription can be
            // born live and scheduled, skipping them would leave a new integration that looks
            // configured and never runs: stale in the consumers' cache, absent from the scheduler.
            await _BitweenCache.BroadcastRevoke();
            await _subScheduler.Sync(entity, Array.Empty<Schedule>());

            return entity.Id;
        }

        private class Validate : AbstractValidator<SubscriptionCreate>
        {
            public Validate(AdapterRequirements adapterRequirements)
            {
                RuleFor(i => i.Name).NotEmpty();
                RuleFor(i => i.DocumentId).NotEmpty().When(i => i.Type != SubscriptionType.Aggregation);
                RuleFor(i => i.PartnerId).NotEqual(Partner.SystemId);
                RuleFor(i => i.Type).NotEqual(SubscriptionType.Unknown);

                When(i => (i.Type != SubscriptionType.Receiving && i.Type != SubscriptionType.GatewayApiCall && i.Type != SubscriptionType.BusGateway), () => { RuleFor(i => i.PartnerId).NotEmpty(); });

                When(i => i.Type == SubscriptionType.GatewayApiCall || i.Type == SubscriptionType.BusGateway,
                    () =>
                    {
                        RuleFor(i => i.PartnerId)
                            .Null()
                            .WithMessage(model => $"PartnerId must be null for {model.Type} subscriptions");
                    });

                When(i => i.Type == SubscriptionType.Aggregation,
                    () => { RuleFor(i => i.AggregationForId).NotEmpty(); });

                // An adapter that is named has to be usable. Nothing is required to be named —
                // create without a pipeline is still legitimate — but a half-configured adapter
                // is the failure this endpoint exists to stop committing.
                RuleFor(i => i.ReceiverProperties).CustomAsync(async (provided, context, _) =>
                    await AddMissing(context, adapterRequirements,
                        ((SubscriptionCreate)context.InstanceToValidate).ReceiverId, provided));

                RuleFor(i => i.ValidatorProperties).CustomAsync(async (provided, context, _) =>
                    await AddMissing(context, adapterRequirements,
                        ((SubscriptionCreate)context.InstanceToValidate).ValidatorId, provided));

                RuleFor(i => i.MapperProperties).CustomAsync(async (provided, context, _) =>
                    await AddMissing(context, adapterRequirements,
                        ((SubscriptionCreate)context.InstanceToValidate).MapperId, provided));

                RuleFor(i => i.HandlerProperties).CustomAsync(async (provided, context, _) =>
                    await AddMissing(context, adapterRequirements,
                        ((SubscriptionCreate)context.InstanceToValidate).HandlerId, provided));

                // Schedules only mean anything on the two scheduled types, and an empty set is
                // what Subscription.SetSchedules rejects outright.
                RuleFor(i => i.Schedules)
                    .NotEmpty()
                    .When(i => i.Schedules != null &&
                               (i.Type == SubscriptionType.Receiving || i.Type == SubscriptionType.Aggregation))
                    .WithMessage("Schedules cannot be empty for a scheduled subscription.");
            }

            private static async Task AddMissing(ValidationContext<SubscriptionCreate> context,
                AdapterRequirements adapterRequirements, string adapterId, ICollection<KeyAndValue> provided)
            {
                var missing = await adapterRequirements.MissingFor(adapterId, provided);
                if (missing.Count > 0)
                    context.AddFailure($"Missing: {string.Join(",", missing)}");
            }
        }
    }
}
