using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Notifiers
{
    public class Get: IGetHandler<int>
    {
        private readonly InfolinkDbContext dbContext;

        public Get(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }
        
        public async Task<object> Handle(int key, bool lookup = false)
        {
            var notifier = await dbContext.Set<Notifier>().FirstOrDefaultAsync(n => n.Id == key);
            if (notifier == null) throw new SWNotFoundException();

            var subscriptions = new List<Subscription> { };
            if (notifier.RunOnSubscriptions != null && notifier.RunOnSubscriptions.Any())
            {
                subscriptions = await dbContext.Set<Subscription>()
                    .Where(s => notifier.RunOnSubscriptions.Any(sub => sub == s.Id))
                    .ToListAsync();
            }
           
            
            return new
            {
                Id = notifier.Id,
                Name = notifier.Name,
                Inactive = notifier.Inactive,
                HandlerId = notifier.HandlerId,
                HandlerProperties= notifier.HandlerProperties?.ToKeyAndValueCollection(),
                RunOnSuccessfulResult= notifier.RunOnSuccessfulResult,
                RunOnBadResult = notifier.RunOnBadResult,
                RunOnFailedResult = notifier.RunOnFailedResult,
                RunOnSubscriptions = notifier.RunOnSubscriptions?.Select(r => new NotifierSubscription
                {
                    Id = r,
                    Name = subscriptions?.FirstOrDefault(s => s.Id == r)?.Name
                }).ToList()
            };
        }
    }
}