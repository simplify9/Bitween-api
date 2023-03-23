using SW.EfCoreExtensions;
using SW.PrimitiveTypes;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SW.Infolink
{
    internal class BusService : IConsume
    {
        private const string MessageTypeNameToDocumentId = "MessageTypeNameToDocumentId";

        private readonly XchangeService _xchangeService;
        private readonly InfolinkDbContext _dbContext;
        private readonly RequestContext _requestContext;


        public BusService(XchangeService xchangeService, InfolinkDbContext dbContext,
            RequestContext requestContext)
        {
            _xchangeService = xchangeService;
            _dbContext = dbContext;
            _requestContext = requestContext;
        }

        public async Task<IEnumerable<string>> GetMessageTypeNames()
        {
            var map = await GetMessageTypeNameToDocumentIdMap();
            return map.Keys;
        }

        public async Task Process(string messageTypeName, string message)
        {
            var map = await GetMessageTypeNameToDocumentIdMap();

            var xf = new XchangeFile(message);

            await _xchangeService.SubmitFilterXchange(map[messageTypeName], xf, null, _requestContext.CorrelationId);
        }

        private async Task<IReadOnlyDictionary<string, int>> GetMessageTypeNameToDocumentIdMap()
        {
            return (await _dbContext.ListAsync(new BusEnabledDocuments())).ToDictionary(k => k.BusMessageTypeName,
                v => v.Id);
        }
    }
}