using System.Threading.Tasks;
using FluentValidation;
using SW.Infolink.Domain;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Notifiers
{
    public class Create:ICommandHandler<NotifierCreate>
    {
        private readonly InfolinkDbContext dbContext;

        public Create(InfolinkDbContext dbContext)
        {
            this.dbContext = dbContext;
        }
        
        public async Task<object> Handle(NotifierCreate request)
        {
            var notifier = new Notifier(request.Name);
            
            dbContext.Add(notifier);
            await dbContext.SaveChangesAsync();
            return notifier.Id;
        }
        
        private class Validate : AbstractValidator<NotifierCreate>
        {
            public Validate()
            {
                RuleFor(i => i.Name).NotEmpty();
            }
        }
    }
}