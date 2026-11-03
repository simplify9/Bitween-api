using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Services;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// A request whose query runs past the database's time limit answers 504 with
/// <c>DATABASE_TIMED_OUT</c>: the admin UI doesn't ask again for it, and says what happened.
/// </summary>
[Collection("Bitween")]
public class DatabaseTimeoutTests(BitweenFixture fixture)
{
    [Fact]
    public async Task A_query_past_the_database_time_limit_is_answered_as_a_timeout()
    {
        // What real timeouts throw, rather than exceptions made up to look like them: past the
        // connection's Command Timeout, past the database's own statement_timeout, and cancelled by
        // the caller, which is not one.
        Exception commandTimedOut, statementTimedOut, cancelled;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            db.Database.SetCommandTimeout(1);
            commandTimedOut = await Record.ExceptionAsync(() => db.Database.ExecuteSqlRawAsync("SELECT pg_sleep(5)"));
        }

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SET LOCAL statement_timeout = 500");
            statementTimedOut = await Record.ExceptionAsync(() => db.Database.ExecuteSqlRawAsync("SELECT pg_sleep(5)"));
        }

        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            cancelled = await Record.ExceptionAsync(() => db.Database.ExecuteSqlRawAsync("SELECT pg_sleep(5)", cancel.Token));
        }

        Assert.True(DatabaseTimeoutMiddleware.IsDatabaseTimeout(statementTimedOut), statementTimedOut?.ToString());
        Assert.False(DatabaseTimeoutMiddleware.IsDatabaseTimeout(cancelled), cancelled?.ToString());

        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        await Answer(context, commandTimedOut);

        Assert.Equal(StatusCodes.Status504GatewayTimeout, context.Response.StatusCode);
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
        context.Response.Body.Position = 0;
        Assert.Contains(DatabaseTimeoutMiddleware.Code, await new StreamReader(context.Response.Body).ReadToEndAsync());

        // Anything else fails as it did, a timeout outside the database included.
        await Assert.ThrowsAsync<InvalidOperationException>(() => Answer(new DefaultHttpContext(), new InvalidOperationException()));
        await Assert.ThrowsAsync<TimeoutException>(() => Answer(new DefaultHttpContext(), new TimeoutException()));
    }

    private static Task Answer(HttpContext context, Exception thrown) =>
        new DatabaseTimeoutMiddleware(_ => throw thrown, NullLogger<DatabaseTimeoutMiddleware>.Instance)
            .InvokeAsync(context);
}
