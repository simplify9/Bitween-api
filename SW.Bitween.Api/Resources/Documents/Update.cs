using System.Linq;
using SW.EfCoreExtensions;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
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
            _requestContext.EnsureAccess(AccountRole.Admin, AccountRole.Member);

            var entity = await _dbContext.FindAsync<Document>(key);

            if (string.IsNullOrWhiteSpace(model.Name))
                throw new SWValidationException("INVALID_NAME", "Give the information type a name.");

            var nameDuplicated = await _dbContext.Set<Document>()
                .AsNoTracking()
                .Where(i => i.Id != key)
                .AnyAsync(i => i.Name == model.Name);
            if (nameDuplicated)
                throw new SWValidationException("NAME_TAKEN", "An information type with this name already exists.");

            if (!Regex.IsMatch(model.Code ?? "", "^[A-Z][A-Z0-9_]{1,49}$"))
                throw new SWValidationException("INVALID_CODE",
                    "Codes are upper-case letters, digits and underscores (2-50 chars).");

            var codeDuplicated = await _dbContext.Set<Document>()
                .AsNoTracking()
                .Where(i => i.Id != key)
                .AnyAsync(i => i.Code == model.Code);
            if (codeDuplicated)
                throw new SWValidationException("CODE_TAKEN", "This code is already in use.");

            var busTypeNameDuplicated = await _dbContext.Set<Document>()
                .AsNoTracking()
                .Where(i => i.Id != key)
                .Where(i => !string.IsNullOrEmpty(i.BusMessageTypeName))
                .Where(i => i.BusMessageTypeName == model.BusMessageTypeName)
                .AnyAsync();

            if (busTypeNameDuplicated)
                throw new SWValidationException("DUPLICATED_BUS_TYPE_NAME",
                    "Cant use duplicated bus Message type name");

            if (model.PromotedProperties != null)
            {
                foreach (var pp in model.PromotedProperties)
                {
                    if (string.IsNullOrWhiteSpace(pp.Key))
                        throw new SWValidationException("INVALID_PROMOTED_PROPERTY_KEY",
                            "Promoted property key cannot be null or empty.");

                    if (string.IsNullOrWhiteSpace(pp.Value))
                        throw new SWValidationException("INVALID_PROMOTED_PROPERTY_VALUE",
                            $"Promoted property '{pp.Key}' must have a non-empty path value.");

                    if (model.DocumentFormat == DocumentFormat.Json)
                    {
                        // Must be a JSONPath: starts with '$' or a simple dot-separated identifier path
                        var trimmed = pp.Value.Trim();
                        if (!trimmed.StartsWith("$") && !Regex.IsMatch(trimmed, @"^[a-zA-Z_][a-zA-Z0-9_]*(?:(\.[a-zA-Z_][a-zA-Z0-9_]*)|(\[[0-9]+\]))*$"))
                            throw new SWValidationException("INVALID_PROMOTED_PROPERTY_PATH",
                                $"Promoted property '{pp.Key}' has an invalid JSON path: '{pp.Value}'. Expected a JSONPath expression (e.g. '$.field.subField') or dot-notation path.");
                    }
                    else if (model.DocumentFormat == DocumentFormat.Xml)
                    {
                        // Basic XPath sanity: must start with '/' or '//' or be a valid element path
                        var trimmed = pp.Value.Trim();
                        if (!trimmed.StartsWith("/") && !Regex.IsMatch(trimmed, @"^[a-zA-Z_][a-zA-Z0-9_/\[\]@.:*-]*$"))
                            throw new SWValidationException("INVALID_PROMOTED_PROPERTY_PATH",
                                $"Promoted property '{pp.Key}' has an invalid XML path: '{pp.Value}'. Expected an XPath expression (e.g. '/root/element').");
                    }
                }

                var duplicateKey = model.PromotedProperties
                    .GroupBy(pp => pp.Key, System.StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(g => g.Count() > 1)?.Key;

                if (duplicateKey != null)
                    throw new SWValidationException("DUPLICATE_PROMOTED_PROPERTY_KEY",
                        $"Promoted property key '{duplicateKey}' appears more than once.");
            }

            var trail = new DocumentTrail(DocumentTrailCode.Updated, entity);
            entity.SetDictionaries(model.PromotedProperties.ToDictionary());
            // Name/Code have private setters — SetProperties only writes public-setter
            // properties, so it silently no-ops on these two (verified empirically).
            entity.SetName(model.Name);
            entity.SetCode(model.Code);
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