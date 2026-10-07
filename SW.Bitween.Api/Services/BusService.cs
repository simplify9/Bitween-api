using SW.Bitween.Domain;
using SW.EfCoreExtensions;
using SW.PrimitiveTypes;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Bitween
{
    public class BusService(XchangeService xchangeService, BitweenDbContext dbContext,
        RequestContext requestContext) : IConsume
    {
        private const string MessageTypeNameToDocumentId = "MessageTypeNameToDocumentId";

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

            await xchangeService.SubmitFilterXchange(map[messageTypeName], xf, null, requestContext.CorrelationId,
                string.IsNullOrWhiteSpace(sourceXchangeId) ? null : new XchangeSource(sourceXchangeId));
        }

        private async Task<IReadOnlyDictionary<string, int>> GetMessageTypeNameToDocumentIdMap()
        {
            return (await dbContext.ListAsync(new BusEnabledDocuments())).ToDictionary(k => k.BusMessageTypeName,
                v => v.Id);
        }
    }
}