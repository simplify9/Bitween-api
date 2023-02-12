using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.PrimitiveTypes;
using SW.Infolink.Model;

namespace SW.Infolink
{
    public class FilterService
    {
        readonly IInfolinkCache _cache;

        public FilterService(IInfolinkCache cache)
        {
            this._cache = cache;
        }

        public async Task<FilterResult> Filter(int documentId, XchangeFile xchangeFile)
        {
            if (xchangeFile is null)
                throw new ArgumentNullException(nameof(xchangeFile));

            var doc = await _cache.DocumentByIdAsync(documentId);

            IExchangePayloadReader propReader = doc.DocumentFormat == DocumentFormat.Xml
                ? new XmlExchangePayloadReader(xchangeFile.Data)
                : new JsonExchangePayloadReader(xchangeFile.Data);

            var filterResult = new FilterResult();

            foreach (var pp in doc.PromotedProperties)
            {
                propReader.TryGetValue(pp.Value, out var ppValue);
                filterResult.Properties.Add(pp.Key, ppValue?.ToLower());
            }

            var subs = await _cache.ListSubscriptionsByDocumentAsync(documentId);

            var matches = subs.Where(sub =>
            {
                var exp = sub.BackwardCompatibleMatchExpression(doc);
                return exp == null || exp.IsMatch(propReader);
            }).ToArray();

            foreach (var subscription in matches)
            {
                filterResult.Hits.Add(subscription.Id);
            }

            return filterResult;
        }
    }
}