using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using SW.Bitween.Resources.RetryPolicies;

namespace SW.Bitween.Resources.Subscriptions
{
    public class Update : ICommandHandler<int, SubscriptionUpdate, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly IInfolinkCache _BitweenCache;
        private readonly RequestContext _requestContext;
        private readonly SubscriptionSchedulerService _subScheduler;

        public Update(BitweenDbContext dbContext, IInfolinkCache BitweenCache, RequestContext requestContext, SubscriptionSchedulerService subScheduler)
        {
            this._dbContext = dbContext;
            _BitweenCache = BitweenCache;
            _requestContext = requestContext;
            _subScheduler = subScheduler;
        }

        public async Task<object> Handle(int key, SubscriptionUpdate model)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Subscriptions.Edit);
            var entity = await _dbContext.FindAsync<Subscription>(key);

            // Capture before SetSchedules replaces the collection.
            var oldSchedules = entity.Schedules.ToList();

            var trail = new SubscriptionTrail(SubscriptionTrialCode.Updated, entity);

            // Name and the runtime-state fields, which only an update may set.
            _dbContext.Entry(entity).SetProperties(model);

            if (model.CustomRetryPolicy == null && model.RetryPolicyId != null &&
                !await _dbContext.Set<RetryPolicy>().AnyAsync(p => p.Id == model.RetryPolicyId))
                throw new SWValidationException("RETRY_POLICY_NOT_FOUND",
                    $"Retry policy {model.RetryPolicyId} was not found.");

            if (model.CustomRetryPolicy != null)
                RetryGroupValidation.EnsureCanFire(model.CustomRetryPolicy.Groups);

            // Everything a person configures, through the same code the create handler runs.
            await SubscriptionConfigurationApplier.Apply(_dbContext, entity, model);

            trail.SetAfter(entity);
            _dbContext.Add(trail);
            await _dbContext.SaveChangesAsync();
            await _BitweenCache.BroadcastRevoke();

            // Sync Quartz: unschedule removed entries, schedule new/kept ones.
            await _subScheduler.Sync(entity, oldSchedules);

            return null;
        }

        // private static Dictionary<string, string> ReplaceHiddenData(IReadOnlyDictionary<string, string> original,
        //     Dictionary<string, string> updated)
        // {
        //     foreach (var item in updated.Where(item => item.Value.StartsWith("encrypted__")))
        //     {
        //         updated[item.Key] = original[item.Key];
        //     }
        //
        //     return updated;
        // }
        //
        // private static Dictionary<string, string> EncryptValues(IReadOnlyDictionary<string, string> original,
        //     Dictionary<string, string> updated)
        // {
        //     foreach (var item in original)
        //     {
        //         if (item.Key.StartsWith("encrypted__"))
        //         {
        //             updated[item.Key] = original[item.Key];
        //         }
        //     }
        //
        //     return updated;
        // }

        private static bool ValidateMatch(IPropertyMatchSpecification model)
        {
            if (model is null)
                return true;
            return model switch
            {
                NotOneOfSpec notOneOfSpec => !string.IsNullOrEmpty(notOneOfSpec.Path) && notOneOfSpec.Values.Any(),
                OneOfSpec oneOfSpec => !string.IsNullOrEmpty(oneOfSpec.Path) && oneOfSpec.Values.Any(),
                AndSpec andSpec => ValidateMatch(andSpec.Left) && ValidateMatch(andSpec.Right),
                OrSpec orSpec => ValidateMatch(orSpec.Left) && ValidateMatch(orSpec.Right),
                _ => false
            };
        }

        private class Validate : AbstractValidator<SubscriptionUpdate>
        {
            private ValueTask<Subscription> GetSub(BitweenDbContext dbContext, IHttpContextAccessor httpContextAccessor)
            {
                var path = httpContextAccessor.HttpContext?.Request.Path.Value;

                var lastSegment = path?
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .LastOrDefault();
                if (lastSegment is null || !int.TryParse(lastSegment, out var subId))
                    return new ValueTask<Subscription>((Subscription)null);

                return dbContext.FindAsync<Subscription>(subId);
            }
            public Validate(BitweenDbContext dbContext, IHttpContextAccessor httpContextAccessor, AdapterRequirements adapterRequirements)
            {
                RuleFor(i => i.Name).NotEmpty();
                RuleFor(i => i.MatchExpression).Must(ValidateMatch);
                RuleFor(i => i.PartnerId).NotEqual(Partner.SystemId);
                // Matches the rule Documents enforces on BusMessageTypeName; see Create.
                RuleFor(i => i.ResponseMessageTypeName)
                    .Matches("^\\S+$")
                    .When(i => !string.IsNullOrEmpty(i.ResponseMessageTypeName))
                    .WithMessage("A bus message name cannot contain spaces.");

                When(i => i.MapperId != null, () =>
                {
                    RuleFor(i => i.MapperProperties).CustomAsync(async (provided, context, _) =>
                    {
                        var missing = await adapterRequirements.MissingFor(
                            ((SubscriptionUpdate)context.InstanceToValidate).MapperId, provided);
                        if (missing.Count > 0)
                            context.AddFailure($"Missing: {string.Join(",", missing)}");
                    });
                });

                When(i => i.HandlerId != null, () =>
                {
                    RuleFor(i => i.HandlerProperties).CustomAsync(async (provided, context, _) =>
                    {
                        var missing = await adapterRequirements.MissingFor(
                            ((SubscriptionUpdate)context.InstanceToValidate).HandlerId, provided);
                        if (missing.Count > 0)
                            context.AddFailure($"Missing: {string.Join(",", missing)}");
                    });

                    RuleFor(i => i.ResponseSubscriptionId).CustomAsync(async (responseSubId, context, ct) =>
                    {
                        if (!responseSubId.HasValue) return;
                        var subscription = await GetSub(dbContext, httpContextAccessor);
                        if (subscription != null && responseSubId.Value == subscription.Id)
                            context.AddFailure(nameof(SubscriptionUpdate.ResponseSubscriptionId), "ResponseSubscriptionId cannot be the same as the current subscription.");
                    });
                });

                // Outside the handler check above: a response destination that can never work is
                // wrong whether or not this same request also sets a handler.
                RuleFor(i => i.ResponseSubscriptionId).CustomAsync(async (responseSubId, context, ct) =>
                {
                    var failure = await ResponseRoutingValidation.CheckDestination(dbContext, responseSubId);
                    if (failure != null)
                        context.AddFailure(nameof(SubscriptionUpdate.ResponseSubscriptionId), failure);
                });

                RuleFor(i => i).CustomAsync(async (model, context, ct) =>
                {
                    var subscription = await GetSub(dbContext, httpContextAccessor);

                    if (subscription?.Type == SubscriptionType.Receiving)
                    {
                        if (string.IsNullOrEmpty(model.ReceiverId))
                            context.AddFailure(nameof(model.ReceiverId), "ReceiverId is required for Receiving subscriptions");

                        if (model.Schedules == null || !model.Schedules.Any())
                            context.AddFailure(nameof(model.Schedules), "Schedules are required for Receiving subscriptions");

                        var missing = await adapterRequirements.MissingFor(model.ReceiverId, model.ReceiverProperties);
                        if (missing.Count > 0)
                            context.AddFailure(nameof(model.ReceiverProperties), $"Missing properties: {string.Join(",", missing)}");
                    }
                });

                RuleFor(i => i).CustomAsync(async (model, context, ct) =>
                {
                    var subscription = await GetSub(dbContext, httpContextAccessor);

                    if (subscription?.Type == SubscriptionType.Aggregation)
                    {
                        if (model.Schedules == null || !model.Schedules.Any())
                            context.AddFailure(nameof(model.Schedules), "Schedules are required for Aggregation subscriptions");

                        if (!model.AggregationForId.HasValue)
                            context.AddFailure(nameof(model.AggregationForId), "AggregationForId is required for Aggregation subscriptions");
                    }
                });

                RuleFor(i => i).CustomAsync(async (model, context, ct) =>
                {

                    var subscription = await GetSub(dbContext, httpContextAccessor);

                    if (subscription?.Type == SubscriptionType.GatewayApiCall ||
                        subscription?.Type == SubscriptionType.BusGateway)
                    {
                        if (model.PartnerId.HasValue)
                            context.AddFailure(nameof(model.PartnerId), $"PartnerId must be null for {subscription?.Type} subscriptions");
                    }
                });

            }
        }
    }
}