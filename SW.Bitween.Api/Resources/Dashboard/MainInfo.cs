using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Domain.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Dashboard;

[HandlerName("MainInfo")]
public class MainInfo : IQueryHandler<object>
{
    private readonly BitweenDbContext _dbContext;

    public MainInfo(BitweenDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<object> Handle()
    {
        var subscriptionsCount = await _dbContext.Set<Subscription>().AsNoTracking().CountAsync();
        var documentCount = await _dbContext.Set<Document>().AsNoTracking().CountAsync();
        var notifiersCount = await _dbContext.Set<Notifier>().AsNoTracking().CountAsync();
        var usersCount = await _dbContext.Set<Account>().AsNoTracking().CountAsync();
        var partnersCount = await _dbContext.Set<Partner>().AsNoTracking().CountAsync();


        return new
        {
            subscriptionsCount,
            documentCount,
            notifiersCount,
            usersCount,
            partnersCount,
            LastUpdated = DateTime.UtcNow

        };
    }
}