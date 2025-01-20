using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Xchanges
{
    [HandlerName("bulkretry")]
    public class BulkRetry : ICommandHandler<XchangeBulkRetry,object>
    {
        private readonly BitweenDbContext _dbContext;
        private readonly XchangeService _xchangeService;


        public BulkRetry(BitweenDbContext dbContext, XchangeService xchangeService)
        {
            _dbContext = dbContext;
            _xchangeService = xchangeService;
        }

        public async Task<object> Handle(XchangeBulkRetry request)
        {
            var xchanges = await _dbContext.Set<Xchange>().Where(c => request.Ids.Contains(c.Id)).AsNoTracking()
                .ToListAsync();

            foreach (var xchange in xchanges)
            {
                var inputFileData = await _xchangeService.GetFile(xchange.Id, XchangeFileType.Input);
                var xchangeFile = new XchangeFile(inputFileData, xchange.InputName);
                if (request.Reset)
                {
                    var subscription = await _dbContext.FindAsync<Subscription>(xchange.SubscriptionId);
                    if (subscription == null)
                        throw new SWValidationException("SUBSCRIPTION_NOT_FOUND",
                            "Cant reset properties, subscription doesnt exist anymore");
                    await _xchangeService.CreateXchange(subscription, xchange, xchangeFile);
                }
                else
                {
                    await _xchangeService.CreateXchange(xchange, xchangeFile);
                }
            }

            await _dbContext.SaveChangesAsync();

            return null;
        }
    }
}