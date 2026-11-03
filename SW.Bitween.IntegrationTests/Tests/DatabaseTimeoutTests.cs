using System;
using System.IO;
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
        // What a real timeout throws, rather than an exception made up to look like one.
        Exception timedOut;
        await using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            db.Database.SetCommandTimeout(1);
            timedOut = await Record.ExceptionAsync(() => db.Database.ExecuteSqlRawAsync("SELECT pg_sleep(5)"));
        }

        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        await Answer(context, timedOut);

        Assert.Equal(StatusCodes.Status504GatewayTimeout, context.Response.StatusCode);
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
