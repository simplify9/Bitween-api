using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain.Accounts;

namespace SW.Bitween.Services;

/// <summary>
/// Keeps a partner's people inside the partner portal: their requests reach <c>/api/portal</c> and
/// the few self-service endpoints signing in needs, and nothing else - not the admin API, not /mcp.
/// </summary>
/// <remarks>
/// <para>
/// Enforced here, once, rather than left to each handler. Partner roles hold no admin permission,
/// but many admin reads ask for none, and every new handler would be one more place to forget.
/// The portal's own handlers then scope to the caller's partner themselves.
/// </para>
/// <para>
/// Read from the account rather than from a claim, so it holds for every way of signing in, whatever
/// each puts in its token. An account's partner never changes, so each node remembers the answer
/// for good after the first request.
/// </para>
/// </remarks>
public class PartnerPortalGate(RequestDelegate next)
{
    public const string PortalPath = "/api/portal";

    /// <summary>Who an account is, for the gate. Never changes once known: a partner stays one, and a removed account stays removed.</summary>
    private enum Side { Ours, Partner, Gone }

    private static readonly ConcurrentDictionary<int, Side> SideByAccount = new();

    /// <summary>What a partner's person may call outside the portal: signing in and out, and themselves.</summary>
    private static readonly HashSet<string> SelfService = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/accounts/login",
        "/api/accounts/logout",
        "/api/accounts/profile",
        "/api/accounts/changepassword",
        "/api/settings/config",
        "/api/settings/myversion",
    };

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments(Mcp.McpTokenHandler.Path)) &&
            !Allowed(context.Request.Path) &&
            int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId) &&
            // A token can outlive its account - an AI app's, say, is checked by its signature alone. With
            // no account behind it, it is refused here too, rather than reaching reads that ask for no
            // permission.
            await SideOfAsync(context, accountId) is Side.Partner or Side.Gone)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }

    // A file link carries its own signature, which is what lets it be opened at all; the portal
    // hands out links only to the files of the partner's own exchanges.
    private static bool Allowed(PathString path) =>
        path.StartsWithSegments(PortalPath) || path.StartsWithSegments("/" + FileLinks.RoutePrefix) ||
        SelfService.Contains(path.Value!.TrimEnd('/'));

    private static async Task<Side> SideOfAsync(HttpContext context, int accountId)
    {
        if (SideByAccount.TryGetValue(accountId, out var known)) return known;

        var db = context.RequestServices.GetRequiredService<BitweenDbContext>();
        var account = await db.Set<Account>().AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new { a.PartnerId, a.Deleted })
            .SingleOrDefaultAsync();
        var side = account is null || account.Deleted ? Side.Gone : account.PartnerId is null ? Side.Ours : Side.Partner;
        SideByAccount[accountId] = side;
        return side;
    }
}
