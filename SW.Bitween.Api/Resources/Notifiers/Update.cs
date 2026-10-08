using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Notifiers
{
public class Update(BitweenDbContext dbContext, RequestContext requestContext, IInfolinkCache cache)
        : ICommandHandler<int, NotifierUpdate,object>
    {
        public async Task<object> Handle(int key, NotifierUpdate request)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Notifiers.Edit);

            var notifier = await dbContext.FindAsync<Notifier>(key);

            // A sentinel means "keep the stored secret", which only makes sense for the handler it
            // was stored for. Switching handlers drops it, so the secret can't follow the notifier
            // to a handler that might send it somewhere else.
            var newHandlerId = request.HandlerId ?? notifier.HandlerId;
            var incoming = (request.HandlerProperties ?? []).ToDictionary();
            var stored = AdapterSecretProperties.MayKeepStoredSecrets(notifier.HandlerId, newHandlerId,
                notifier.HandlerProperties, incoming)
                ? notifier.HandlerProperties
                : null;

            notifier.Update(request.Name, request.RunOnSuccessfulResult,
                request.RunOnBadResult,
                request.RunOnFailedResult,
                newHandlerId,
                request.Inactive,
                request.RunOnSubscriptions?.Select(r => r.Id)?.ToArray());

            // An absent list means none, as it does for a document's promoted properties
            // and a retry policy's groups. Left implicit it threw ArgumentNullException.
            notifier.SetDictionaries(
                AdapterSecretProperties.Merge(stored, incoming));

            await dbContext.SaveChangesAsync();
            await cache.BroadcastRevoke();
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