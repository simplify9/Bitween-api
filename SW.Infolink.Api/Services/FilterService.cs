using System;
using System.Linq;
using System.Threading.Tasks;
using SW.PrimitiveTypes;
using SW.Infolink.Model;

namespace SW.Infolink
{
    public class FilterService
    {
        private readonly IInfolinkCache _infolinkCache;

        public FilterService(IInfolinkCache infolinkCache)
        {
            _infolinkCache = infolinkCache;
        }
        
        public async Task<FilterResult> Filter(int documentId, XchangeFile xchangeFile)
        {
            if (xchangeFile is null)
                throw new InfolinkException("Invalid file.");
            var doc = await _infolinkCache.DocumentByIdAsync(documentId);

            IPropertyReader propReader = doc.DocumentFormat == DocumentFormat.Xml
                ? new XmlPropertyReader(xchangeFile.Data)
                : new JsonPropertyReader(xchangeFile.Data);

            var filterResult = new FilterResult();

            foreach (var pp in doc.PromotedProperties)
            {
                propReader.TryGetValue(pp.Value, out var ppValue);
                filterResult.Properties.Add(pp.Key, ppValue.ToLower());
            }

            var subs = await _infolinkCache.ListSubscriptionsByDocumentAsync(documentId);
            

            var matches = subs?.Where(sub =>
                {
                    var exp = sub.BackwardCompatibleMatchExpression(doc);
                    return exp == null || exp.IsMatch(propReader);
                }).ToList();
            
            if (matches == null || !matches.Any()) return filterResult;
            
            foreach (var subscription in matches)
            {
                filterResult.Hits.Add(subscription.Id);
            }

            return filterResult;
        }
    }
}


