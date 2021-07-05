using System.Threading.Tasks;
using FluentValidation;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Notifiers
{
    public class Update:ICommandHandler<int,NotifierUpdate>
    {
        private readonly InfolinkDbContext dbContext;

        public Update(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }
        
        public async Task<object> Handle(int key, NotifierUpdate request)
        {

            var notifier = await dbContext.FindAsync<Notifier>(key);
            
            
             notifier.Update(request.Name, request.RunOnSuccessfulResult,
                request.RunOnBadResult,
                request.RunOnFailedResult,
                request.HandlerId ?? notifier.HandlerId,
                request.Inactive);
             
             notifier.SetDictionaries(request.HandlerProperties.ToDictionary());
            
            
            await dbContext.SaveChangesAsync();
            return null;
        }
        
        private class Validate : AbstractValidator<NotifierUpdate>
        {
            public Validate()
            {
                RuleFor(i => i.Name).NotEmpty();
                RuleFor(i => i.HandlerId).NotEmpty();
            }
        }
    }
}