using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System;
using System.Linq;
using System.Threading.Tasks;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Resources.Subscriptions
{
    public class Update : ICommandHandler<int, SubscriptionUpdate,object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly IInfolinkCache _BitweenCache;
        private readonly RequestContext _requestContext;

        public Update(BitweenDbContext dbContext, IInfolinkCache BitweenCache, RequestContext requestContext)
        {
            this._dbContext = dbContext;
            _BitweenCache = BitweenCache;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(int key, SubscriptionUpdate model)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);
            var entity = await _dbContext.FindAsync<Subscription>(key);

            var trail = new SubscriptionTrail(SubscriptionTrialCode.Updated, entity);
            _dbContext.Entry(entity).SetProperties(model);

            entity.SetSchedules(model.Schedules.Select(dto => new Schedule(dto.Recurrence,
                TimeSpan.Parse($"{dto.Days}.{dto.Hours}:{dto.Minutes}:0"), dto.Backwards)).ToList());
            entity.SetDictionaries(
                model.HandlerProperties.ToDictionary(),
                model.MapperProperties.ToDictionary(),
                model.ReceiverProperties.ToDictionary(),
                model.DocumentFilter.ToDictionary(),
                model.ValidatorProperties.ToDictionary()
            );
            entity.SetMatchExpression(model.MatchExpression);


            trail.SetAfter(entity);
            _dbContext.Add(trail);
            await _dbContext.SaveChangesAsync();
            _BitweenCache.BroadcastRevoke();
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
                NotOneOfSpec notOneOfSpec => !string.IsNullOrEmpty(notOneOfSpec.Name) && notOneOfSpec.Values.Any(),
                OneOfSpec oneOfSpec => !string.IsNullOrEmpty(oneOfSpec.Name) && oneOfSpec.Values.Any(),
                AndSpec andSpec => ValidateMatch(andSpec.Left) && ValidateMatch(andSpec.Right),
                OrSpec orSpec => ValidateMatch(orSpec.Left) && ValidateMatch(orSpec.Right),
                _ => false
            };
        }

        private class Validate : AbstractValidator<SubscriptionUpdate>
        {
            public Validate(IServiceProvider serviceProvider)
            {
                RuleFor(i => i.Name).NotEmpty();
                RuleFor(i => i.MatchExpression).Must(ValidateMatch);
                RuleFor(i => i.PartnerId).NotEqual(Partner.SystemId);

                When(i => i.MapperId != null, () =>
                {
                    RuleFor(i => i.MapperProperties).CustomAsync(async (i, context, ct) =>
                    {
                        var mapperId = ((SubscriptionUpdate)context.InstanceToValidate).MapperId;
                        var mustProps = Enumerable.Empty<string>();

                        // Check if it's a native adapter
                        if (mapperId.StartsWith("native.", StringComparison.OrdinalIgnoreCase))
                        {
                            var nativeAdapterDiscovery = serviceProvider.GetService<NativeAdapterDiscoveryService>();
                            var properties = nativeAdapterDiscovery.GetNativeAdapterProperties(mapperId);
                            mustProps = properties.Where(p => p.Value.EndsWith(" *")).Select(p => p.Key);
                        }
                        else
                        {
                            var serverless = serviceProvider.GetService<IServerlessService>();
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
                        if (handlerId.StartsWith("native.", StringComparison.OrdinalIgnoreCase))
                        {
                            var nativeAdapterDiscovery = serviceProvider.GetService<NativeAdapterDiscoveryService>();
                            var properties = nativeAdapterDiscovery.GetNativeAdapterProperties(handlerId);
                            mustProps = properties.Where(p => p.Value.EndsWith(" *")).Select(p => p.Key);
                        }
                        else
                        {
                            var serverless = serviceProvider.GetService<IServerlessService>();
                            await serverless.StartAsync(handlerId, null);
                            mustProps = (await serverless.GetExpectedStartupValues())
                                .Where(p => p.Value.Optional == false).Select(p => p.Key);
                        }

                        var missing = mustProps.ToHashSet(StringComparer.OrdinalIgnoreCase)
                            .Except(i.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => p.Key));
                        if (missing.Any())
                            context.AddFailure($"Missing: {string.Join(",", missing)}");
                    });
                });

                RuleFor(i => i).CustomAsync(async (model, context, ct) =>
                {
                    var dbContext = serviceProvider.GetService<BitweenDbContext>();
                    var subscription = await dbContext.FindAsync<Subscription>(new object[] { context.RootContextData["Key"] }, ct);
                    
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
                            if (model.ReceiverId.StartsWith("native.", StringComparison.OrdinalIgnoreCase))
                            {
                                var nativeAdapterDiscovery = serviceProvider.GetService<NativeAdapterDiscoveryService>();
                                var properties = nativeAdapterDiscovery.GetNativeAdapterProperties(model.ReceiverId);
                                mustProps = properties.Where(p => p.Value.EndsWith(" *")).Select(p => p.Key);
                            }
                            else
                            {
                                var serverless = serviceProvider.GetService<IServerlessService>();
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
                    var dbContext = serviceProvider.GetService<BitweenDbContext>();
                    var subscription = await dbContext.FindAsync<Subscription>(new object[] { context.RootContextData["Key"] }, ct);
                    
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
                    var dbContext = serviceProvider.GetService<BitweenDbContext>();
                    var subscription = await dbContext.FindAsync<Subscription>(new object[] { context.RootContextData["Key"] }, ct);
                    
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