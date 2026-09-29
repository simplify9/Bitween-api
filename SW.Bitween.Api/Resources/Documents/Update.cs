using System.Linq;
using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using SW.Bitween.Resources.Ops;

namespace SW.Bitween.Resources.Documents
{
    public class Update(BitweenDbContext dbContext, IInfolinkCache BitweenCache, RequestContext requestContext,
        IBroadcast broadcast, BrokerQueues brokerQueues) : ICommandHandler<int, DocumentUpdate, object>
    {
        public async Task<object> Handle(int key, DocumentUpdate model)
        {
            await requestContext.EnsurePermission(dbContext, Model.Permissions.Documents.Edit);

            var entity = await dbContext.FindAsync<Document>(key);

            if (string.IsNullOrWhiteSpace(model.Name))
                throw new SWValidationException("INVALID_NAME", "Give the information type a name.");

            // Ignoring case, as Create does: two types whose names differ only in case
            // are indistinguishable in every list that shows them.
            var wantedName = model.Name.ToLower();
            var nameDuplicated = await dbContext.Set<Document>()
                .AsNoTracking()
                .Where(i => i.Id != key)
                .AnyAsync(i => i.Name.ToLower() == wantedName);
            if (nameDuplicated)
                throw new SWValidationException("NAME_TAKEN", "An information type with this name already exists.");

            var code = string.IsNullOrWhiteSpace(model.Code) ? null : model.Code;

            if (code != null && !Regex.IsMatch(code, "^[A-Z][A-Z0-9_]{1,49}$"))
                throw new SWValidationException("INVALID_CODE",
                    "Codes are upper-case letters, digits and underscores (2-50 chars).");

            if (code != null)
            {
                var codeDuplicated = await dbContext.Set<Document>()
                    .AsNoTracking()
                    .Where(i => i.Id != key)
                    .AnyAsync(i => i.Code == code);
                if (codeDuplicated)
                    throw new SWValidationException("CODE_TAKEN", "This code is already in use.");
            }

            if (!string.IsNullOrEmpty(model.BusMessageTypeName) && Regex.IsMatch(model.BusMessageTypeName, @"\s"))
                throw new SWValidationException("INVALID_BUS_TYPE_NAME",
                    "Bus message type name cannot contain spaces.");

            await BusMessageTypeNames.EnsureFree(dbContext, model.BusMessageTypeName, key);

            PromotedPropertyValidation.Check(model.PromotedProperties, model.DocumentFormat);

            var oldBusMessageTypeName = entity.BusMessageTypeName;

            // An absent list means none, the same as it does for retry policy groups.
            // Left implicit it threw ArgumentNullException — a 500 for a request the
            // API had simply never decided the meaning of.
            entity.SetDictionaries((model.PromotedProperties ?? []).ToDictionary());
            // Name/Code have private setters — SetProperties only writes public-setter
            // properties, so it silently no-ops on these two (verified empirically).
            entity.SetName(model.Name);
            entity.SetCode(code);
            // The route key is what identifies the type; the body carries an Id too, and
            // SetProperties copies it straight onto the tracked entity. A caller that omits it
            // sends 0, which EF rejects as an attempt to change a primary key — a 500 for a
            // request that was perfectly well formed. Normalising it here makes the copy a no-op
            // whatever the body said.
            model.Id = key;
            dbContext.Entry(entity).SetProperties(model);

            await dbContext.SaveChangesAsync();
            await BitweenCache.BroadcastRevoke();
            await broadcast.RefreshConsumers();

            // A new name is a new queue, so the old one goes, and so does sending none: that takes
            // the type off the bus for good. Turning the bus off with the name kept is a pause, not
            // a rename: the queue stays, holding what arrives until the bus is turned back on.
            if (!string.IsNullOrWhiteSpace(oldBusMessageTypeName)
                && !string.Equals(oldBusMessageTypeName, entity.BusMessageTypeName, System.StringComparison.OrdinalIgnoreCase))
                await brokerQueues.DeleteInformationTypeLane(oldBusMessageTypeName);
            return null;
        }
    }
}