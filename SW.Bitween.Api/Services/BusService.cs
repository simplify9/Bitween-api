using SW.Bitween.Domain;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween
{
    public class BusService(XchangeService xchangeService, IInfolinkCache cache,
        RequestContext requestContext, BitweenOptions options) : IConsume
    {
        private const string MessageTypeNameToDocumentId = "MessageTypeNameToDocumentId";

        private static readonly System.Func<Document, bool> BusEnabled = new BusEnabledDocuments().Criteria.Compile();

        public async Task<IEnumerable<string>> GetMessageTypeNames()
        {
            var map = await GetMessageTypeNameToDocumentIdMap();
            return map.Keys;
        }

        public async Task Process(string messageTypeName, string message)
        {
            var map = await GetMessageTypeNameToDocumentIdMap();

            var xf = new XchangeFile(message);

            // A Bitween delivery's response says which exchange delivered, for a route to read that
            // exchange's input by. A message published any other way has none.
            var sourceXchangeId = requestContext.GetValueOf(StartupValuesFiller.SourceXchangeBusValue,
                RequestValueType.ServiceBusValue);

            // A mirror's rehearsal keeps its correlation across the bus, which would start a new one.
            var rehearsal = requestContext.GetValueOf(Services.Mirror.MirrorTraffic.BusValue, RequestValueType.ServiceBusValue);
            var correlationId = Services.Mirror.MirrorTraffic.IsRehearsal(options, rehearsal) ? rehearsal : requestContext.CorrelationId;

            await xchangeService.SubmitFilterXchange(map[messageTypeName], xf, null, correlationId,
                string.IsNullOrWhiteSpace(sourceXchangeId) ? null : new XchangeSource(sourceXchangeId));
        }

        /// <summary>
        /// From the cached documents, which every save to a document revokes: this was a query per
        /// message on the bus.
        /// </summary>
        private async Task<IReadOnlyDictionary<string, int>> GetMessageTypeNameToDocumentIdMap()
        {
            return (await cache.ListDocumentsAsync()).Where(BusEnabled)
                .ToDictionary(k => k.BusMessageTypeName, v => v.Id);
        }
    }
}