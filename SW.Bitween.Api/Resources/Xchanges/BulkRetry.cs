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
    public class BulkRetry : ICommandHandler<XchangeBulkRetry, object>
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
            var scheduledIds = await _dbContext.Set<DelayedRetry>()
                .Where(d => request.Ids.Contains(d.Id))
                .Select(d => d.Id)
                .ToListAsync();

            var xchanges = await _dbContext.Set<Xchange>()
                .Where(c => request.Ids.Contains(c.Id) && !scheduledIds.Contains(c.Id)).AsNoTracking()
                .ToListAsync();

            foreach (var xchange in xchanges)
            {
                var inputFileData = await _xchangeService.GetFile(xchange.Id, XchangeFileType.Input);
                var xchangeFile = new XchangeFile(inputFileData, xchange.InputName);
                var subscription = await _dbContext.Subscriptions()
                    .FirstOrDefaultAsync(s => s.Id == xchange.SubscriptionId);
                
                if (request.Reset)
                {
                    if (subscription == null)
                        throw new SWValidationException("SUBSCRIPTION_NOT_FOUND",
                            "Cant reset properties, subscription doesnt exist anymore");
                    await _xchangeService.CreateXchange(subscription, xchange, xchangeFile,
                        manualRetry: true);
                }
                else
                {
                    
                    // Null when the subscription has since been deleted, which a document-only
                    // exchange also has from the start. The single-exchange retry has always allowed
                    // for it; without the same here, one such id in a selection threw and took the
                    // whole bulk retry down with it.
                    await _xchangeService.CreateXchange(xchange, xchangeFile, subscription?.WorkGroup,
                        manualRetry: true);
                }
            }

            await _dbContext.SaveChangesAsync();

            return null;
        }
    }
}