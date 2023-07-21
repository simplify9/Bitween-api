using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Infolink.Domain;
using SW.Infolink.Domain.Accounts;
using SW.PrimitiveTypes;

namespace SW.Infolink.Resources.Dashboard;

[HandlerName("MainInfo")]
public class MainInfo : IQueryHandler
{
    private readonly InfolinkDbContext _dbContext;

    public MainInfo(InfolinkDbContext dbContext)
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