using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain.Accounts;
using SW.PrimitiveTypes;

namespace SW.Bitween.IntegrationTests.Fixtures;

internal static class TestRequestContext
{
    /// <summary>
    /// Handlers resolve their permissions from the database, and no account is signed in during
    /// these tests. The break-glass superuser claim grants the whole catalog, so a test exercises
    /// the handler rather than the guard in front of it.
    /// </summary>
    public static RequestContext Superuser(this AsyncServiceScope scope)
    {
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();
        ctx.Set(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(Bitween.RequestContextExtensions.SuperuserClaim, "true")], "integration-test")));
        return ctx;
    }

    /// <summary>
    /// Signs in as a real account, so guards resolve that account's actual roles from the database.
    /// The counterpart to <see cref="Superuser"/>: use this whenever the guard itself is what the
    /// test is about, since the superuser claim grants the whole catalog and would hide a denial.
    /// </summary>
    public static RequestContext As(this AsyncServiceScope scope, int accountId)
    {
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();
        ctx.Set(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, accountId.ToString())], "integration-test")));
        return ctx;
    }

    /// <summary>
    /// Creates a fresh Viewer account and signs in as it, for tests that need a real denial rather
    /// than a contrived one. A new account each time because the collection shares a database and
    /// email is unique; the caller gets the id back for assertions.
    /// </summary>
    public static async Task<int> AsNewViewer(this AsyncServiceScope scope, string label)
    {
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var viewer = new Account("Viewer", $"{label}@test.local", "hash", AccountRole.Viewer);
        db.Set<Account>().Add(viewer);
        await db.SaveChangesAsync();

        db.Set<AccountRoleLink>().Add(new AccountRoleLink(viewer.Id, Role.ViewerId));
        await db.SaveChangesAsync();

        scope.As(viewer.Id);
        return viewer.Id;
    }

    /// <summary>Signed in, but with no account behind the token — grants must resolve to nothing.</summary>
    public static RequestContext AsAnonymous(this AsyncServiceScope scope)
    {
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();
        ctx.Set(new ClaimsPrincipal(new ClaimsIdentity([], "integration-test")));
        return ctx;
    }
}
