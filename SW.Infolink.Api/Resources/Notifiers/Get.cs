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
            var query = from notifier in dbContext.Set<Notifier>()
                where notifier.Id == key
                select new
                {
                    Id = notifier.Id,
                    Name = notifier.Name,
                    Inactive = notifier.Inactive,
                    HandlerId = notifier.HandlerId,
                    HandlerProperties= notifier.HandlerProperties.ToKeyAndValueCollection(),
                    RunOnSuccessfulResult= notifier.RunOnSuccessfulResult,
                    RunOnBadResult = notifier.RunOnBadResult,
                    RunOnFailedResult = notifier.RunOnFailedResult
                };
                
            
            return await query.AsNoTracking().SingleOrDefaultAsync();
        }
    }
}