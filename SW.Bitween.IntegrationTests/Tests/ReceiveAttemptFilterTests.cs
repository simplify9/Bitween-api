using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// The <c>ReceiveAttemptId</c> search filter: "the exchanges one run created".
/// </summary>
/// <remarks>
/// It exists so the UI can link to a run's exchanges without naming every one of them in the URL,
/// which put a ceiling on how big a run could be linked to. The filter is resolved before the
/// query rather than as a subquery, because <c>ReceiveAttempt.ExchangeIds</c> is stored as one
/// separator-delimited string and no provider can see inside it.
/// </remarks>
[Collection("Bitween")]
public class ReceiveAttemptFilterTests(BitweenFixture fixture)
{
    private static SearchyRequest ForAttempt(int attemptId) =>
        new("ReceiveAttemptId", SearchyRule.EqualsTo, attemptId.ToString());

    /// <summary>A subscription with one exchange, and a run that claims to have created it.</summary>
    private static async Task<(int AttemptId, string XchangeId)> ARunThatCreated(
        BitweenDbContext db, XchangeService xs, string name)
    {
        var doc = new Document(null, name, DocumentFormat.Json);
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var sub = new Subscription(name, doc.Id) { Inactive = false };
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();

        var xchange = await xs.CreateXchange(sub, new XchangeFile("{}"));
        await db.SaveChangesAsync();

        var attempt = new ReceiveAttempt
        {
            SubscriptionId = sub.Id,
            StartedOn = System.DateTime.UtcNow,
            FinishedOn = System.DateTime.UtcNow,
            Outcome = ReceiveOutcome.Received,
            ExchangeIds = [xchange.Id],
        };
        db.Set<ReceiveAttempt>().Add(attempt);
        await db.SaveChangesAsync();

        return (attempt.Id, xchange.Id);
    }

    private static async Task<string[]> Search(AsyncServiceScope scope, SearchyRequest request)
    {
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var response = (SearchyResponse<XchangeRow>)await new Resources.Xchanges.Search(
            db, xs, scope.Superuser()).Handle(request);
        return response.Result.Select(r => r.Id).ToArray();
    }

    [Fact]
    public async Task Returns_the_run_s_own_exchanges_and_nobody_else_s()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var mine = await ARunThatCreated(db, xs, "Attempt filter mine");
        // A second run, so "returns everything" cannot pass by accident.
        var theirs = await ARunThatCreated(db, xs, "Attempt filter theirs");

        var found = await Search(scope, ForAttempt(mine.AttemptId));

        Assert.Equal([mine.XchangeId], found);
        Assert.DoesNotContain(theirs.XchangeId, found);
    }

    [Fact]
    public async Task A_run_that_created_nothing_matches_nothing_rather_than_everything()
    {
        await using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        // Exchanges exist; this run just is not responsible for any of them. Dropping the filter
        // instead of honouring it would return all of them — and bulk retry acts on this selection.
        await ARunThatCreated(db, xs, "Attempt filter bystander");

        var empty = new ReceiveAttempt
        {
            SubscriptionId = 1,
            StartedOn = System.DateTime.UtcNow,
            FinishedOn = System.DateTime.UtcNow,
            Outcome = ReceiveOutcome.NoNewData,
            ExchangeIds = [],
        };
        db.Set<ReceiveAttempt>().Add(empty);
        await db.SaveChangesAsync();

        Assert.Empty(await Search(scope, ForAttempt(empty.Id)));
    }

    [Fact]
    public async Task A_run_that_no_longer_exists_is_refused()
    {
        await using var scope = fixture.CreateScope();

        // Refused rather than ignored, for the same reason: a silently dropped filter widens the
        // selection to every exchange. ReceiveAttemptCleanupJob does delete old runs, so a stale
        // link is a real case, not a hypothetical one.
        var ex = await Assert.ThrowsAsync<SWValidationException>(
            () => Search(scope, ForAttempt(int.MaxValue)));

        Assert.Contains(ex.Validations, v => v.Key == "NOT_FOUND");
    }
}
