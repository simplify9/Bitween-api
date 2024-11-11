using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.PrimitiveTypes;
using SW.EfCoreExtensions;
using System.Linq;
using System.Threading.Tasks;
using SW.Infolink.Model;

namespace SW.Infolink.Resources.SubscriptionDraft
{
    [HandlerName("BySubscription")]
    public class GetBySubscription : IGetHandler<int>
    {
        private readonly InfolinkDbContext _dbContext;

        public GetBySubscription(InfolinkDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<object> Handle(int key, bool lookup = false)
        {
            var subs =
                await _dbContext.Set<Domain.SubscriptionDraft>()
                    .Where(i => i.SubscriptionId == key)
                    .ToListAsync();


            return new
            {
                Result = subs.Select(subscriber => new DraftSubscription
                {
                    Id = subscriber.Id,
                    DocumentFilter = subscriber.DocumentFilter.ToKeyAndValueCollection(),
                    HandlerId = subscriber.HandlerId,
                    MapperId = subscriber.MapperId,
                    
                    ReceiverId = subscriber.ReceiverId,
                    MapperProperties = subscriber.MapperProperties.ToKeyAndValueCollection(),
                    HandlerProperties = subscriber.HandlerProperties.ToKeyAndValueCollection(),
                    ReceiverProperties = subscriber.ReceiverProperties.ToKeyAndValueCollection(),
                    ValidatorProperties = subscriber.ValidatorProperties.ToKeyAndValueCollection(),
                    ResponseSubscriptionId = subscriber.ResponseSubscriptionId,
                    ValidatorId = subscriber.ValidatorId,
                    MatchExpression = subscriber.MatchExpression,
                    CategoryDescription = subscriber.Category?.Description,
                    CategoryCode = subscriber.Category?.Code,
                    CategoryId = subscriber.CategoryId,
                    Schedules = subscriber.Schedules.Select(s => new ScheduleView
                    {
                        Backwards = s.Backwards,
                        Recurrence = s.Recurrence,
                        Days = s.On.Days,
                        Hours = s.On.Hours,
                        Minutes = s.On.Minutes
                    }).ToList(),
                    Type = subscriber.Type,
                }),
                TotalCount = subs.Count
            };
        }
    }
}