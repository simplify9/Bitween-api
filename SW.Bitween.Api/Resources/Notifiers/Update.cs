using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Notifiers
{
    public class Update : ICommandHandler<int, NotifierUpdate,object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly RequestContext _requestContext;

        public Update(BitweenDbContext dbContext, RequestContext requestContext)
        {
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<object> Handle(int key, NotifierUpdate request)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Notifiers.Edit);

            var notifier = await _dbContext.FindAsync<Notifier>(key);

            notifier.Update(request.Name, request.RunOnSuccessfulResult,
                request.RunOnBadResult,
                request.RunOnFailedResult,
                request.HandlerId ?? notifier.HandlerId,
                request.Inactive,
                request.RunOnSubscriptions?.Select(r => r.Id)?.ToArray());

            // An absent list means none, as it does for a document's promoted properties
            // and a retry policy's groups. Left implicit it threw ArgumentNullException.
            notifier.SetDictionaries((request.HandlerProperties ?? []).ToDictionary());


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