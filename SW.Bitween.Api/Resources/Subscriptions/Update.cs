using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Resources.Subscriptions
{
    public class Update : ICommandHandler<int, SubscriptionUpdate, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly IInfolinkCache _BitweenCache;
        private readonly RequestContext _requestContext;
        private readonly SubscriptionSchedulerService _subScheduler;

        private const string PrivateSentinel = "__private__";

        public Update(BitweenDbContext dbContext, IInfolinkCache BitweenCache, RequestContext requestContext, SubscriptionSchedulerService subScheduler)
        {
            this._dbContext = dbContext;
            _BitweenCache = BitweenCache;
            _requestContext = requestContext;
            _subScheduler = subScheduler;
        }

        public async Task<object> Handle(int key, SubscriptionUpdate model)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);
            var entity = await _dbContext.FindAsync<Subscription>(key);

            // Capture before SetSchedules replaces the collection.
            var oldSchedules = entity.Schedules.ToList();

            var trail = new SubscriptionTrail(SubscriptionTrialCode.Updated, entity);
            _dbContext.Entry(entity).SetProperties(model);

            entity.SetSchedules(model.Schedules.Select(dto => new Schedule(dto.Recurrence,
                TimeSpan.Parse($"{dto.Days}.{dto.Hours}:{dto.Minutes}:0"), dto.Backwards)).ToList());
            entity.SetDictionaries(
                MergeWithOriginal(entity.HandlerProperties, model.HandlerProperties),
                MergeWithOriginal(entity.MapperProperties, model.MapperProperties),
                MergeWithOriginal(entity.ReceiverProperties, model.ReceiverProperties),
                model.DocumentFilter.ToDictionary(),
                MergeWithOriginal(entity.ValidatorProperties, model.ValidatorProperties)
            );
            entity.SetMatchExpression(model.MatchExpression);


            trail.SetAfter(entity);
            _dbContext.Add(trail);
            await _dbContext.SaveChangesAsync();
            await _BitweenCache.BroadcastRevoke();

            // Sync Quartz: unschedule removed entries, schedule new/kept ones.
            await _subScheduler.Sync(entity, oldSchedules);

            return null;
        }

        private static System.Collections.Generic.Dictionary<string, string> MergeWithOriginal(
            IReadOnlyDictionary<string, string> original,
            ICollection<KeyAndValue> incoming)
        {
            var result = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var kv in incoming ?? Enumerable.Empty<KeyAndValue>())
            {
                if (kv.Value == PrivateSentinel)
                {
                    // Private prop: restore the original stored value, don't overwrite with sentinel
                    if (original != null && original.TryGetValue(kv.Key, out var stored))
                        result[kv.Key] = stored;
                }
                else
                {
                    result[kv.Key] = kv.Value;
                }
            }
            return result;
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
                NotOneOfSpec notOneOfSpec => !string.IsNullOrEmpty(notOneOfSpec.Name) && notOneOfSpec.Values.Any(),
                OneOfSpec oneOfSpec => !string.IsNullOrEmpty(oneOfSpec.Name) && oneOfSpec.Values.Any(),
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
            public Validate(BitweenDbContext dbContext, IHttpContextAccessor httpContextAccessor, NativeAdapterDiscoveryService nativeAdapterDiscovery, IServiceProvider serviceProvider)
            {
                RuleFor(i => i.Name).NotEmpty();
                RuleFor(i => i.MatchExpression).Must(ValidateMatch);
                RuleFor(i => i.PartnerId).NotEqual(Partner.SystemId);

                When(i => i.MapperId != null, () =>
                {
                    RuleFor(i => i.MapperProperties).CustomAsync(async (i, context, _) =>
                    {
                        var mapperId = ((SubscriptionUpdate)context.InstanceToValidate).MapperId;
                        var mustProps = Enumerable.Empty<string>();

                        // Check if it's a native adapter
                        if (mapperId.StartsWith(NativeAdapterDiscoveryService.NativePrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            var properties = nativeAdapterDiscovery.GetStartupValues(mapperId);
                            mustProps = properties.Where(p => !p.Value.Optional).Select(p => p.Key);
                        }
                        else
                        {
                            var serverless = serviceProvider.GetRequiredService<IServerlessService>();
                            await serverless.StartAsync(mapperId, null);
                            mustProps = (await serverless.GetExpectedStartupValues())
                                .Where(p => p.Value.Optional == false).Select(p => p.Key);
                        }

                        var missing = mustProps.ToHashSet(StringComparer.OrdinalIgnoreCase)
                            .Except(i.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => p.Key));
                        if (missing.Any())
                            context.AddFailure($"Missing: {string.Join(",", missing)}");
                    });
                });

                When(i => i.HandlerId != null, () =>
                {

                    RuleFor(i => i.HandlerProperties).CustomAsync(async (i, context, ct) =>
                    {
                        var handlerId = ((SubscriptionUpdate)context.InstanceToValidate).HandlerId;
                        var mustProps = Enumerable.Empty<string>();

                        // Check if it's a native adapter
                        if (handlerId.StartsWith(NativeAdapterDiscoveryService.NativePrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            var properties = nativeAdapterDiscovery.GetStartupValues(handlerId);
                            mustProps = properties.Where(p => !p.Value.Optional).Select(p => p.Key);
                        }
                        else
                        {
                            var serverless = serviceProvider.GetRequiredService<IServerlessService>();
                            await serverless.StartAsync(handlerId, null);
                            mustProps = (await serverless.GetExpectedStartupValues())
                                .Where(p => p.Value.Optional == false).Select(p => p.Key);
                        }

                        var missing = mustProps.ToHashSet(StringComparer.OrdinalIgnoreCase)
                            .Except(i.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => p.Key));
                        if (missing.Any())
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

                RuleFor(i => i).CustomAsync(async (model, context, ct) =>
                {
                    var subscription = await GetSub(dbContext, httpContextAccessor);

                    if (subscription?.Type == SubscriptionType.Receiving)
                    {
                        if (string.IsNullOrEmpty(model.ReceiverId))
                            context.AddFailure(nameof(model.ReceiverId), "ReceiverId is required for Receiving subscriptions");

                        if (model.Schedules == null || !model.Schedules.Any())
                            context.AddFailure(nameof(model.Schedules), "Schedules are required for Receiving subscriptions");

                        if (!string.IsNullOrEmpty(model.ReceiverId))
                        {
                            var mustProps = Enumerable.Empty<string>();

                            // Check if it's a native adapter
                            if (model.ReceiverId.StartsWith(NativeAdapterDiscoveryService.NativePrefix, StringComparison.OrdinalIgnoreCase))
                            {
                                var properties = nativeAdapterDiscovery.GetStartupValues(model.ReceiverId);
                                mustProps = properties.Where(p => !p.Value.Optional).Select(p => p.Key);
                            }
                            else
                            {
                                var serverless = serviceProvider.GetRequiredService<IServerlessService>();
                                await serverless.StartAsync(model.ReceiverId, null);
                                mustProps = (await serverless.GetExpectedStartupValues())
                                    .Where(p => p.Value.Optional == false).Select(p => p.Key);
                            }

                            var missing = mustProps.ToHashSet(StringComparer.OrdinalIgnoreCase)
                                .Except(model.ReceiverProperties.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => p.Key));
                            if (missing.Any())
                                context.AddFailure(nameof(model.ReceiverProperties), $"Missing properties: {string.Join(",", missing)}");
                        }
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

                    if (subscription?.Type == SubscriptionType.GatewayApiCall)
                    {
                        if (model.PartnerId.HasValue)
                            context.AddFailure(nameof(model.PartnerId), "PartnerId must be null for GatewayApiCall subscriptions");
                    }
                });

            }
        }
    }
}