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
}
