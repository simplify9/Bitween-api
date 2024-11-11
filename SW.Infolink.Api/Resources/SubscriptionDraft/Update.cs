using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SW.EfCoreExtensions;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain.Accounts;

namespace SW.Infolink.Resources.SubscriptionDraft
{
    public class Update : ICommandHandler<int, DraftSubscription>
    {
        private readonly InfolinkDbContext _dbContext;
        private readonly IInfolinkCache _infolinkCache;
        private readonly RequestContext _requestContext;

        public Update(InfolinkDbContext dbContext, IInfolinkCache infolinkCache, RequestContext requestContext)
        {
            this._dbContext = dbContext;
            _infolinkCache = infolinkCache;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(int key, DraftSubscription model)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);
            var entity = await _dbContext.Set<Domain.SubscriptionDraft>().FirstOrDefaultAsync(i => i.Id == key);

            if (entity is null)
                throw new SWValidationException("DRAFT_SUBSCRIPTION_WAS_NOT_FOUND",
                    $"A draft subscription with id {key} was not found");

            entity.SetSchedules(model.Schedules.Select(dto => new Schedule(dto.Recurrence,
                TimeSpan.Parse($"{dto.Days}.{dto.Hours}:{dto.Minutes}:0"), dto.Backwards)).ToList());
            entity.CategoryId = model.CategoryId;
            entity.ResponseSubscriptionId = model.ResponseSubscriptionId;
            entity.ResponseMessageTypeName = model.ResponseMessageTypeName;
            entity.MapperId = model.MapperId;
            entity.ReceiverId = model.ReceiverId;
            entity.ValidatorId = model.ValidatorId;
            entity.HandlerId = model.HandlerId;
            entity.SetDictionaries(
                model.HandlerProperties.ToDictionary(),
                model.MapperProperties.ToDictionary(),
                model.ReceiverProperties.ToDictionary(),
                model.DocumentFilter.ToDictionary(),
                model.ValidatorProperties.ToDictionary()
            );
            entity.SetMatchExpression(model.MatchExpression);
            await _dbContext.SaveChangesAsync();
            _infolinkCache.BroadcastRevoke();
            return null;
        }


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

        private class Validate : AbstractValidator<DraftSubscription>
        {
            public Validate(IServiceProvider serviceProvider)
            {
                RuleFor(i => i.MatchExpression).Must(ValidateMatch);

                When(i => i.MapperId != null, () =>
                {
                    RuleFor(i => i.MapperProperties).CustomAsync(async (i, context, ct) =>
                    {
                        var serverless = serviceProvider.GetService<IServerlessService>();
                        await serverless.StartAsync(((DraftSubscription)context.InstanceToValidate).MapperId, null);
                        var mustProps = (await serverless.GetExpectedStartupValues())
                            .Where(p => p.Value.Optional == false).Select(p => p.Key);
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
                        var serverless = serviceProvider.GetService<IServerlessService>();
                        await serverless.StartAsync(((DraftSubscription)context.InstanceToValidate).HandlerId, null);
                        var mustProps = (await serverless.GetExpectedStartupValues())
                            .Where(p => p.Value.Optional == false).Select(p => p.Key);
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
                            var serverless = serviceProvider.GetService<IServerlessService>();
                            await serverless.StartAsync(((DraftSubscription)context.InstanceToValidate).ReceiverId,
                                null);
                            var mustProps = (await serverless.GetExpectedStartupValues())
                                .Where(p => p.Value.Optional == false).Select(p => p.Key);
                            var missing = mustProps.ToHashSet(StringComparer.OrdinalIgnoreCase)
                                .Except(i.Where(p => !string.IsNullOrEmpty(p.Value)).Select(p => p.Key));
                            if (missing.Any())
                                context.AddFailure($"Missing properties: {string.Join(",", missing)}");
                        });
                    });
                });

                When(i => i.Type == SubscriptionType.Aggregation, () => { RuleFor(i => i.Schedules).NotEmpty(); });
            }
        }
    }
}