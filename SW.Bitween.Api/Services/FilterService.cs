using System;
using System.Linq;
using System.Threading.Tasks;
using SW.PrimitiveTypes;
using SW.Bitween.Model;

namespace SW.Bitween
{
    public class FilterService
    {
        readonly IInfolinkCache _cache;

        public FilterService(IInfolinkCache cache)
        {
            _cache = cache;
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

            if (!propReader.CanGetValues())
                return filterResult;

            foreach (var pp in doc.PromotedProperties)
            {
                propReader.TryGetValue(pp.Value, out var ppValue);
                //TODO check if we need to validate here
                //if (ppValue is null)
                //  throw new SWValidationException("PROMOTED_PROPERTY_NOT_FOUND", $"The path {pp.Value} is null on the docuemnt");
                filterResult.Properties.Add(pp.Key, ppValue?.ToLower());
            }

            var subs = await _cache.ListSubscriptionsByDocumentAsync(documentId);

            var matches = subs.Where(sub =>
            {
                // Bus-gateway subscriptions only run via their gateway routes (with the route's
                // filter and optional partner), never through the normal auto-match flow.
                if (sub.Type == SubscriptionType.BusGateway)
                    return false;

                var exp = sub.BackwardCompatibleMatchExpression(doc);
                return exp == null || exp.IsMatch(propReader);
            }).ToArray();

            foreach (var subscription in matches)
            {
                filterResult.Hits.Add(subscription.Id);
            }

            // Bus-gateway routes: run the assigned subscription (optionally with a partner's values)
            // for every route whose filter matches. A null filter matches all messages on the doc.
            var routes = await _cache.ListBusGatewayRoutesByDocumentAsync(documentId);
            foreach (var route in routes)
            {
                if (route.MatchExpression == null || route.MatchExpression.IsMatch(propReader))
                {
                    filterResult.GatewayHits.Add(new GatewayHit
                    {
                        SubscriptionId = route.SubscriptionId,
                        PartnerId = route.PartnerId
                    });
                }
            }

            return filterResult;
        }
    }
}