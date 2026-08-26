using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>Signed in, but with no account behind the token — grants must resolve to nothing.</summary>
    public static RequestContext AsAnonymous(this AsyncServiceScope scope)
    {
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();
        ctx.Set(new ClaimsPrincipal(new ClaimsIdentity([], "integration-test")));
        return ctx;
    }
}
