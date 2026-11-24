using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain;
using SW.Bitween.Model;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Portal;

/// <summary>The signed-in partner's exchanges, newest first, a page at a time.</summary>
/// <remarks>
/// An exchange is the partner's when it carries their id. Exchanges from before that column existed
/// carry none and are not shown: the admin list works theirs out from the subscription, which no
/// index can answer, and a partner's list must stay quick on a table of millions.
/// </remarks>
[HandlerName("xchanges")]
public class Xchanges(BitweenDbContext dbContext, RequestContext requestContext, XchangeService xchangeService)
    : IQueryHandler<PortalXchangeSearch, object>
{
    public const int MaxPageSize = 100;

    public async Task<object> Handle(PortalXchangeSearch request)
    {
        var caller = await PortalCaller.EnsureAsync(dbContext, requestContext, Model.Permissions.Portal.ExchangesView);
        var partnerId = caller.PartnerId;
        var size = Math.Clamp(request.Size, 1, MaxPageSize);
        var page = Math.Max(request.Page, 0);

        var query = from x in dbContext.Set<Xchange>().AsNoTracking()
                    where x.PartnerId == partnerId
                    join r in dbContext.Set<XchangeResult>() on x.Id equals r.Id into xr
                    from r in xr.DefaultIfEmpty()
                    join d in dbContext.Set<Document>() on x.DocumentId equals d.Id
                    join s in dbContext.Set<Subscription>() on x.SubscriptionId equals s.Id into xs
                    from s in xs.DefaultIfEmpty()
                    select new { x, r, DocumentName = d.Name, SubscriptionName = s.Name };

        // On the exchange's own status once it is filled in, which its index answers; before that,
        // from the result, as the Exchanges page does. A bad response still delivered, so it counts
        // as a success here.
        var stateReady = await Services.XchangeStates.XchangeStateReadiness.IsReadyAsync(dbContext);
        query = (request.Status?.ToLowerInvariant(), stateReady) switch
        {
            ("running", true) => query.Where(q => q.x.Status == XchangeStatus.Processing),
            ("success", true) => query.Where(q => q.x.Status == null || q.x.Status == XchangeStatus.BadResponse),
            ("failed", true) => query.Where(q => q.x.Status == XchangeStatus.Failed),
            ("running", false) => query.Where(q => q.r == null),
            ("success", false) => query.Where(q => q.r != null && q.r.Success),
            ("failed", false) => query.Where(q => q.r != null && !q.r.Success),
            _ => query
        };
        if (request.SubscriptionId is { } subscriptionId)
            query = query.Where(q => q.x.SubscriptionId == subscriptionId);
        if (request.From is { } from)
        {
            var utc = Utc(from);
            query = query.Where(q => q.x.StartedOn >= utc);
        }
        if (request.To is { } to)
        {
            var utc = Utc(to);
            query = query.Where(q => q.x.StartedOn <= utc);
        }

        // One more than the page, to tell whether there is a next one without counting them all.
        var found = await query
            .OrderByDescending(q => q.x.StartedOn)
            .Skip(page * size)
            .Take(size + 1)
            .Select(q => new
            {
                q.x.Id,
                q.x.StartedOn,
                q.x.FilesPrefix,
                q.x.InputName,
                q.x.InputSize,
                q.x.SubscriptionId,
                q.DocumentName,
                q.SubscriptionName,
                HasResult = q.r != null,
                Success = q.r != null && q.r.Success,
                FinishedOn = q.r != null ? q.r.FinishedOn : (DateTime?)null,
                Exception = q.r != null ? q.r.Exception : null,
                OutputName = q.r != null ? q.r.OutputName : null,
                OutputSize = q.r != null ? q.r.OutputSize : 0,
                ResponseName = q.r != null ? q.r.ResponseName : null,
                ResponseSize = q.r != null ? q.r.ResponseSize : 0
            })
            .ToListAsync();

        var withErrors = caller.Can(Model.Permissions.Portal.ExchangeErrorsView);
        var withFiles = caller.Can(Model.Permissions.Portal.ExchangeFilesView);

        return new PortalXchangePage
        {
            HasMore = found.Count > size,
            Result = found.Take(size).Select(f => new PortalXchangeRow
            {
                Id = f.Id,
                StartedOn = f.StartedOn,
                FinishedOn = f.FinishedOn,
                Status = !f.HasResult ? "running" : f.Success ? "success" : "failed",
                DocumentName = f.DocumentName,
                SubscriptionId = f.SubscriptionId,
                SubscriptionName = f.SubscriptionName,
                Error = withErrors && f.HasResult && !f.Success ? f.Exception : null,
                Files = withFiles
                    ? new[]
                        {
                            File(f.Id, f.FilesPrefix, XchangeFileType.Input, f.InputName, f.InputSize),
                            File(f.Id, f.FilesPrefix, XchangeFileType.Output, f.OutputName, f.OutputSize),
                            File(f.Id, f.FilesPrefix, XchangeFileType.Response, f.ResponseName, f.ResponseSize)
                        }.Where(file => file is not null).ToList()
                    : null
            }).ToList()
        };
    }

    /// <summary>A file the exchange has, or null: an empty size means it was never written.</summary>
    private PortalXchangeFile File(string id, string prefix, XchangeFileType type, string name, int size) =>
        size <= 0
            ? null
            : new PortalXchangeFile
            {
                Kind = type.ToString().ToLowerInvariant(),
                Name = name,
                Size = size,
                Url = xchangeService.FileUrl(id, prefix, type)
            };

    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value
    };
}
