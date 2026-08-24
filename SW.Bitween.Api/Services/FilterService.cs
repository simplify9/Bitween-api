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
                // Stored as the payload sent it. It used to be lower-cased here, which was
                // only ever to pair with the lower-cased term in Xchanges/Search — nothing
                // matches on this dictionary (match expressions read the payload directly),
                // so the one thing it changed was what every screen displays: an order for
                // "Acme Retail" listed as "acme retail". Search now lower-cases the column
                // instead, which keeps it case-insensitive without rewriting the data.
                filterResult.Properties.Add(pp.Key, ppValue);
            }

            var subs = await _cache.ListSubscriptionsByDocumentAsync(documentId);

            var matches = subs.Where(sub =>
            {
                // An integration with an entry point of its own is never started by a document
                // merely arriving on its information type — it is started through that entry
                // point, which is what decides it should run at all:
                //
                //   BusGateway     — its gateway's routes (route filter + optional partner)
                //   Receiving      — its schedule, via ReceivingJob
                //   GatewayApiCall — a partner calling the gateway it is attached to
                //   ApiCall        — its own partner posting to Xchanges/Update, which runs the
                //                    subscription belonging to the caller (legacy GatewayApiCall)
                //
                // All four are started by name, through SubmitSubscriptionXchange. Auto-matching
                // them as well ran them a second time, on traffic addressed to nobody: a scheduled
                // job publishing the very message type it is bound to fed itself forever, and an
                // ApiCall integration belonging to one partner ran on another partner's message.
                // Both stayed hidden only while those handlers happened to be unreachable. The
                // second run also arrived without the partner the entry point would have passed,
                // so every {{partner.…}} in its adapters stayed a literal token.
                //
                // Internal keeps matching. Reacting to a document of its type arriving is the
                // whole definition of the type — it has no other trigger. Aggregation is driven
                // by AggregationJob.
                if (sub.Type is SubscriptionType.BusGateway
                    or SubscriptionType.Receiving
                    or SubscriptionType.GatewayApiCall
                    or SubscriptionType.ApiCall)
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