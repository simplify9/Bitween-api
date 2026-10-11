using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain.DataSources;
using SW.PrimitiveTypes;
using SW.Serverless.Resident;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Bitween.Services.DataSources;

/// <summary>
/// Where a resident adapter's bookmarks actually live - the durable replacement for the in-memory
/// store SW-Serverless registers by default.
///
/// It has to be durable and it has to be shared. A polling database receiver saves the cursor it
/// consumed up to; if that only survives in the host's memory, then a restart replays rows already
/// processed, and a data source that moves to another node replays everything. Neither is a
/// tolerable answer for a receiver whose whole job is to read each row once.
///
/// Registered as a SINGLETON, because the resident host is one - hence the scope created per call
/// rather than an injected <see cref="BitweenDbContext"/>.
/// </summary>
public class BitweenAdapterStateStore(IServiceProvider serviceProvider) : IAdapterStateStore
{
    public async Task<string> GetAsync(AdapterStateKey key, CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var row = await dbContext.Set<AdapterState>().AsNoTracking()
            .FirstOrDefaultAsync(s =>
                s.AdapterId == key.AdapterId &&
                s.InstanceKey == key.InstanceKey &&
                s.Name == key.Name, cancellationToken);

        return row?.Value;
    }

    public async Task SetAsync(AdapterStateKey key, string value, CancellationToken cancellationToken)
    {
        if (value != null && value.Length > AdapterState.MaxValueLength)
            throw new SWException(
                $"Adapter state '{key.Name}' is {value.Length} characters, and the limit is "
                + $"{AdapterState.MaxValueLength}. This holds a bookmark - a cursor, a watermark, a "
                + "small JSON object of them - not data an adapter is staging.");

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var row = await dbContext.Set<AdapterState>()
            .FirstOrDefaultAsync(s =>
                s.AdapterId == key.AdapterId &&
                s.InstanceKey == key.InstanceKey &&
                s.Name == key.Name, cancellationToken);

        if (value == null)
        {
            // Deleting something that was never written is what a first run looks like, not a fault.
            if (row != null) dbContext.Remove(row);
        }
        else if (row == null)
        {
            dbContext.Add(new AdapterState
            {
                AdapterId = key.AdapterId,
                InstanceKey = key.InstanceKey,
                Name = key.Name,
                Value = value,
                UpdatedOn = DateTime.UtcNow
            });
        }
        else
        {
            row.Value = value;
            row.UpdatedOn = DateTime.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
