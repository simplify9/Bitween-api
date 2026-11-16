using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SW.Bitween.Domain.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween.Resources.Accounts;

public class ConnectedAppsRequest
{
    /// <summary>Whose: empty for the caller's own, which anyone may see; another member's needs users.view.</summary>
    public int? AccountId { get; set; }
}

/// <summary>
/// The AI apps an account has signed in with OAuth to use /mcp, one row per app: its name and when
/// its session last renewed. A renewal happens about once a sign-in session's length while the app is
/// in use, so a recent one means it's still using Bitween.
/// </summary>
[HandlerName("connectedapps")]
public class ConnectedApps(BitweenDbContext dbContext, RequestContext requestContext) : IQueryHandler<ConnectedAppsRequest, object>
{
    public async Task<object> Handle(ConnectedAppsRequest request)
    {
        var accountId = await Whose(dbContext, requestContext, request?.AccountId, Model.Permissions.Users.View);
        var active = DateTime.UtcNow - Services.Sessions.IdleLimit;
        var sessions = await dbContext.Set<RefreshToken>().AsNoTracking()
            .Where(t => t.AccountId == accountId && t.ClientId != null && t.SupersededOn == null && t.CreatedOn > active)
            .Select(t => new { t.ClientId, t.ClientName, t.CreatedOn })
            .ToListAsync();

        return sessions.GroupBy(s => s.ClientId)
            .Select(app => new
            {
                ClientId = app.Key,
                Name = app.OrderByDescending(s => s.CreatedOn).First().ClientName,
                LastRenewedOn = app.Max(s => s.CreatedOn),
            })
            .OrderByDescending(app => app.LastRenewedOn)
            .ToList();
    }

    /// <summary>The account asked about: the caller's own, or - with <paramref name="permission"/> - another's.</summary>
    internal static async Task<int> Whose(BitweenDbContext dbContext, RequestContext requestContext, int? accountId, string permission)
    {
        var me = Convert.ToInt32(requestContext.GetNameIdentifier());
        if (accountId is { } other && other != me) await requestContext.EnsurePermission(dbContext, permission);
        return accountId ?? me;
    }
}

public class DisconnectAppRequest
{
    /// <summary>Whose: empty for the caller's own; another member's needs users.edit.</summary>
    public int? AccountId { get; set; }

    public string ClientId { get; set; }
}

/// <summary>
/// Disconnects an AI app: its sessions end, so it can't renew. The access token it holds stays good
/// at /mcp until it expires, a sign-in session's length at most.
/// </summary>
[HandlerName("disconnectapp")]
public class DisconnectApp(BitweenDbContext dbContext, RequestContext requestContext) : ICommandHandler<DisconnectAppRequest, object>
{
    public async Task<object> Handle(DisconnectAppRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.ClientId)) throw new SWValidationException("ClientId", "Which app to disconnect.");
        var accountId = await ConnectedApps.Whose(dbContext, requestContext, request.AccountId, Model.Permissions.Users.Edit);
        await dbContext.Set<RefreshToken>()
            .Where(t => t.AccountId == accountId && t.ClientId == request.ClientId)
            .ExecuteDeleteAsync();
        return null;
    }
}
