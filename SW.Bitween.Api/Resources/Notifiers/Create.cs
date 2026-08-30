using System.Threading.Tasks;
using FluentValidation;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Notifiers
{
    public class Create : ICommandHandler<NotifierCreate,object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;
        private readonly IInfolinkCache _cache;

        public Create(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        {
            this._dbContext = dbContext;
            _requestContext = requestContext;
            _cache = cache;
        }

        public async Task<object> Handle(NotifierCreate request)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Notifiers.Create);

            var notifier = new Notifier(request.Name);

            _dbContext.Add(notifier);
            await _dbContext.SaveChangesAsync();
            await _cache.BroadcastRevoke();
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