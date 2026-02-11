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

                When(i => i.Type == SubscriptionType.Receiving, () =>
                {
                    RuleFor(i => i.ReceiverId).NotEmpty();
                    RuleFor(i => i.Schedules).NotEmpty();

                    When(i => i.ReceiverId != null, () =>
                    {
                        RuleFor(i => i.ReceiverProperties).CustomAsync(async (i, context, ct) =>
                        {
                            var receiverId = ((SubscriptionUpdate)context.InstanceToValidate).ReceiverId;
                            var mustProps = Enumerable.Empty<string>();

                            // Check if it's a native adapter
                            if (receiverId.StartsWith("native.", StringComparison.OrdinalIgnoreCase))
                            {
                                var nativeAdapterDiscovery = serviceProvider.GetService<NativeAdapterDiscoveryService>();
                                var properties = nativeAdapterDiscovery.GetNativeAdapterProperties(receiverId);
                                mustProps = properties.Where(p => p.Value.EndsWith(" *")).Select(p => p.Key);
                            }
                            else
                            {
                                var serverless = serviceProvider.GetService<IServerlessService>();
                                await serverless.StartAsync(receiverId, null);
                                mustProps = (await serverless.GetExpectedStartupValues())
                                    .Where(p => p.Value.Optional == false).Select(p => p.Key);
                            }

                            var missing = mustProps.ToHashSet(StringComparer.OrdinalIgnoreCase)
                                .Except(i.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => p.Key));
                            if (missing.Any())
                                context.AddFailure($"Missing properties: {string.Join(",", missing)}");
                        });
                    });
                });

                When(i => i.Type == SubscriptionType.Aggregation, () =>
                {
                    RuleFor(i => i.Schedules).NotEmpty();
                    RuleFor(i => i.AggregationForId).NotEmpty();
                });
            }
        }
    }
}