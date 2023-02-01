using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SW.Infolink.Resources.Subscriptions
{
    class Update : ICommandHandler<int, SubscriptionUpdate>
    {
        private readonly InfolinkDbContext dbContext;
        private readonly IInfolinkCache _infolinkCache;

        public Update(InfolinkDbContext dbContext, IInfolinkCache infolinkCache)
        {
            this.dbContext = dbContext;
            _infolinkCache = infolinkCache;
        }

        async public Task<object> Handle(int key, SubscriptionUpdate model)
        {
            var entity = await dbContext.FindAsync<Subscription>(key);

            dbContext.Entry(entity).SetProperties(model);

            //entity.SetAggregationSchedules(model.AggregationSchedules.Select(dto => new Schedule(dto.Recurrence, TimeSpan.Parse($"{dto.Days}.{dto.Hours}:{dto.Minutes}:0"), dto.Backwards)).ToList());
            entity.SetSchedules(model.Schedules.Select(dto => new Schedule(dto.Recurrence, TimeSpan.Parse($"{dto.Days}.{dto.Hours}:{dto.Minutes}:0"), dto.Backwards)).ToList());

            entity.SetDictionaries(
                model.HandlerProperties.ToDictionary(),
                model.MapperProperties.ToDictionary(),
                model.ReceiverProperties.ToDictionary(),
                model.DocumentFilter.ToDictionary(),
                model.ValidatorProperties.ToDictionary());

            await dbContext.SaveChangesAsync();
            _infolinkCache.Revoke();
            return null;
        }

        private class Validate : AbstractValidator<SubscriptionUpdate>
        {
            public Validate(IServiceProvider serviceProvider)
            {
                RuleFor(i => i.Name).NotEmpty();

                RuleFor(i => i.PartnerId).NotEqual(Partner.SystemId);

                When(i => i.MapperId != null, () =>
                {
                    RuleFor(i => i.MapperProperties).CustomAsync(async (i, context, ct) =>
                    {
                        var serverless = serviceProvider.GetService<IServerlessService>();
                        await serverless.StartAsync(((SubscriptionUpdate)context.InstanceToValidate).MapperId, null);
                        var mustProps = (await serverless.GetExpectedStartupValues()).Where(p => p.Value.Optional == false).Select(p => p.Key);
                        var missing = mustProps.ToHashSet(StringComparer.OrdinalIgnoreCase).Except(i.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => p.Key));
                        if (missing.Any())
                            context.AddFailure($"Missing: {string.Join(",", missing) }");
                    });
                });

                When(i => i.HandlerId != null, () =>
                {
                    RuleFor(i => i.HandlerProperties).CustomAsync(async (i, context, ct) =>
                    {
                        var serverless = serviceProvider.GetService<IServerlessService>();
                        await serverless.StartAsync(((SubscriptionUpdate)context.InstanceToValidate).HandlerId, null);
                        var mustProps = (await serverless.GetExpectedStartupValues()).Where(p => p.Value.Optional == false).Select(p => p.Key);
                        var missing = mustProps.ToHashSet(StringComparer.OrdinalIgnoreCase).Except(i.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => p.Key));
                        if (missing.Any())
                            context.AddFailure($"Missing: {string.Join(",", missing) }");
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
                            var serverless = serviceProvider.GetService<IServerlessService>();
                            await serverless.StartAsync(((SubscriptionUpdate)context.InstanceToValidate).ReceiverId, null);
                            var mustProps = (await serverless.GetExpectedStartupValues()).Where(p => p.Value.Optional == false).Select(p => p.Key);
                            var missing = mustProps.ToHashSet(StringComparer.OrdinalIgnoreCase).Except(i.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => p.Key));
                            if (missing.Any())
                                context.AddFailure($"Missing properties: {string.Join(",", missing) }");
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
