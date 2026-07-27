using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Notifiers
{
    public class Get: IGetHandler<int,object>
    {
        private readonly BitweenDbContext dbContext;
        private readonly RequestContext requestContext;

        public Get(BitweenDbContext dbContext, RequestContext requestContext)
        {
            this.dbContext = dbContext;
            this.requestContext = requestContext;
        }
        
        public async Task<object> Handle(int key)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Notifiers.View);

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