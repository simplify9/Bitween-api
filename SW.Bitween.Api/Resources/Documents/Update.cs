using System.Linq;
using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace SW.Bitween.Resources.Documents
{
    public class Update : ICommandHandler<int, DocumentUpdate, object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly IInfolinkCache _BitweenCache;
        private readonly RequestContext _requestContext;
        private readonly IBroadcast _broadcast;


        public Update(BitweenDbContext dbContext, IInfolinkCache BitweenCache, RequestContext requestContext,
            IBroadcast broadcast)
        {
            this._dbContext = dbContext;
            _BitweenCache = BitweenCache;
            _requestContext = requestContext;
            _broadcast = broadcast;
        }

        public async Task<object> Handle(int key, DocumentUpdate model)
        {
            await _requestContext.EnsurePermission(_dbContext, Model.Permissions.Documents.Edit);

            var entity = await _dbContext.FindAsync<Document>(key);

            if (string.IsNullOrWhiteSpace(model.Name))
                throw new SWValidationException("INVALID_NAME", "Give the information type a name.");

            // Ignoring case, as Create does: two types whose names differ only in case
            // are indistinguishable in every list that shows them.
            var wantedName = model.Name.ToLower();
            var nameDuplicated = await _dbContext.Set<Document>()
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
                var codeDuplicated = await _dbContext.Set<Document>()
                    .AsNoTracking()
                    .Where(i => i.Id != key)
                    .AnyAsync(i => i.Code == code);
                if (codeDuplicated)
                    throw new SWValidationException("CODE_TAKEN", "This code is already in use.");
            }

            if (!string.IsNullOrEmpty(model.BusMessageTypeName) && Regex.IsMatch(model.BusMessageTypeName, @"\s"))
                throw new SWValidationException("INVALID_BUS_TYPE_NAME",
                    "Bus message type name cannot contain spaces.");

            // Ignoring case, for the reason spelled out in Create: the routing key is
            // lower-cased at both ends, so two names differing only in case are one message.
            var wanted = (model.BusMessageTypeName ?? string.Empty).ToLower();
            var busTypeNameDuplicated = await _dbContext.Set<Document>()
                .AsNoTracking()
                .Where(i => i.Id != key)
                .Where(i => !string.IsNullOrEmpty(i.BusMessageTypeName))
                .Where(i => i.BusMessageTypeName.ToLower() == wanted)
                .AnyAsync();

            if (busTypeNameDuplicated)
                throw new SWValidationException("DUPLICATED_BUS_TYPE_NAME",
                    $"Another information type already publishes as '{model.BusMessageTypeName}'. " +
                    "Names are compared ignoring case, because the bus does.");

            PromotedPropertyValidation.Check(model.PromotedProperties, model.DocumentFormat);

            var trail = new DocumentTrail(DocumentTrailCode.Updated, entity);
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
            _dbContext.Entry(entity).SetProperties(model);

            trail.SetAfter(entity);
            _dbContext.Add(trail);
            await _dbContext.SaveChangesAsync();
            _BitweenCache.BroadcastRevoke();
            await _broadcast.RefreshConsumers();
            return null;
        }
    }
}