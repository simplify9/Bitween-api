using System.Threading.Tasks;
using FluentValidation;
using SW.Infolink.Domain;
using SW.Infolink.Domain.Accounts;
using SW.Infolink.Model;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Notifiers
{
    public class Create : ICommandHandler<NotifierCreate>
    {
        private readonly InfolinkDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public Create(InfolinkDbContext dbContext, RequestContext requestContext)
        {
            this._dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(NotifierCreate request)
        {
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Viewer);

            var notifier = new Notifier(request.Name);

            _dbContext.Add(notifier);
            await _dbContext.SaveChangesAsync();
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