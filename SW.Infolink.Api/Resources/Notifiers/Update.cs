using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using SW.Infolink.Domain;
using SW.Infolink.Domain.Accounts;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Notifiers
{
    public class Update : ICommandHandler<int, NotifierUpdate>
    {
        private readonly InfolinkDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public Update(InfolinkDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(int key, NotifierUpdate request)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Viewer);

            var notifier = await _dbContext.FindAsync<Notifier>(key);

            notifier.Update(request.Name, request.RunOnSuccessfulResult,
                request.RunOnBadResult,
                request.RunOnFailedResult,
                request.HandlerId ?? notifier.HandlerId,
                request.Inactive,
                request.RunOnSubscriptions?.Select(r => r.Id)?.ToArray());

            notifier.SetDictionaries(request.HandlerProperties.ToDictionary());


            await _dbContext.SaveChangesAsync();
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