using System;
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
/// One exchange, at most one retry: the attempts made from an original form a chain that can be
/// read end to end, and retrying anything but its newest attempt is refused.
/// </summary>
[Collection("Bitween")]
public class RetryChainTests
{
    private readonly BitweenFixture _fixture;

    public RetryChainTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<(Subscription sub, Xchange xchange)> FailedXchange(
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

        db.Set<XchangeResult>().Add(new XchangeResult(xchange.Id, null, null, exception: "boom"));
        await db.SaveChangesAsync();

        return (sub, xchange);
    }

    /// <summary>The retry made from <paramref name="id"/>, failed so it can be retried in turn.</summary>
    private static async Task<Xchange> RetryOnce(BitweenDbContext db, XchangeService xs,
        RequestContext ctx, string id)
    {
        await new Resources.Xchanges.Retry(db, ctx, xs).Handle(id, new XchangeRetry { Reset = false });
        await db.SaveChangesAsync();

        var child = await db.Set<Xchange>().FirstAsync(x => x.RetryFor == id);
        db.Set<XchangeResult>().Add(new XchangeResult(child.Id, null, null, exception: "boom again"));
        await db.SaveChangesAsync();
        return child;
    }

    // ─── The invariant ────────────────────────────────────────────────────────

    [Fact]
    public async Task Retry_refuses_an_exchange_that_has_already_been_retried()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var ctx = scope.Superuser();

        var (_, xchange) = await FailedXchange(db, xs, "Chain Refuse Doc");
        var second = await RetryOnce(db, xs, ctx, xchange.Id);

        var error = await Assert.ThrowsAsync<SWValidationException>(() =>
            new Resources.Xchanges.Retry(db, ctx, xs).Handle(xchange.Id, new XchangeRetry()));

        Assert.Contains(second.Id, error.Message);

        // Still exactly one retry from it - the point of the rule.
        Assert.Equal(1, await db.Set<Xchange>().CountAsync(x => x.RetryFor == xchange.Id));
    }

    [Fact]
    public async Task Retry_allows_the_newest_attempt_in_a_chain()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var ctx = scope.Superuser();

        var (_, xchange) = await FailedXchange(db, xs, "Chain Deepen Doc");
        var second = await RetryOnce(db, xs, ctx, xchange.Id);
        var third = await RetryOnce(db, xs, ctx, second.Id);

        Assert.Equal(second.Id, third.RetryFor);
    }

    [Fact]
    public async Task A_scheduled_retry_of_an_already_retried_exchange_is_dropped_with_a_reason()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var ctx = scope.Superuser();

        var (_, xchange) = await FailedXchange(db, xs, "Chain Scheduled Doc");
        var second = await RetryOnce(db, xs, ctx, xchange.Id);

        // Takes a race to arrive at: the manual retry got in after the policy scheduled one. What
        // matters is that the job drops it rather than throwing, which would leave the schedule in
        // place to fail again on every pass.
        var delayed = new DelayedRetry { Id = xchange.Id, On = DateTime.UtcNow.AddMinutes(-1) };
        db.Set<DelayedRetry>().Add(delayed);
        await db.SaveChangesAsync();

        Assert.False(await xs.ExecuteDelayedRetry(delayed));
        await db.SaveChangesAsync();

        Assert.False(await db.Set<DelayedRetry>().AnyAsync(d => d.Id == xchange.Id));
        Assert.Equal(1, await db.Set<Xchange>().CountAsync(x => x.RetryFor == xchange.Id));

        var result = await db.Set<XchangeResult>().AsNoTracking().FirstAsync(r => r.Id == xchange.Id);
        Assert.Contains(second.Id, result.RetryBlockedReason);
    }

    // ─── Bulk retry follows the chain ─────────────────────────────────────────

    [Fact]
    public async Task BulkRetry_retries_the_newest_attempt_of_an_already_retried_selection()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var ctx = scope.Superuser();

        var (_, xchange) = await FailedXchange(db, xs, "Bulk Chain Doc");
        var second = await RetryOnce(db, xs, ctx, xchange.Id);
        var third = await RetryOnce(db, xs, ctx, second.Id);

        // Selecting the original, which is two attempts out of date.
        var plan = (XchangeBulkRetryPlan)await new Resources.Xchanges.BulkRetry(db, ctx, xs)
            .Handle(new XchangeBulkRetry { Ids = [xchange.Id] });
        await db.SaveChangesAsync();

        Assert.Equal(1, plan.WillRetry);
        var substitution = Assert.Single(plan.Substituted);
        Assert.Equal(xchange.Id, substitution.SelectedId);
        Assert.Equal(third.Id, substitution.RetryId);

        Assert.True(await db.Set<Xchange>().AnyAsync(x => x.RetryFor == third.Id));
        Assert.Equal(1, await db.Set<Xchange>().CountAsync(x => x.RetryFor == xchange.Id));
    }

    /// <summary>
    /// Two retries of one exchange at the very same moment both pass the check, since neither has
    /// saved yet. The claim each adds makes the database refuse the second save, so one is kept.
    /// The other retry is played here by a claim saved first, from another context.
    /// </summary>
    [Fact]
    public async Task Of_two_retries_at_the_same_moment_the_database_keeps_one()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var ctx = scope.Superuser();

        var (_, xchange) = await FailedXchange(db, xs, "Retry Race Doc");

        await using (var other = _fixture.CreateScope())
        {
            var otherDb = other.ServiceProvider.GetRequiredService<BitweenDbContext>();
            otherDb.Add(new XchangeRetryClaim(xchange.Id, Guid.NewGuid().ToString("N")));
            await otherDb.SaveChangesAsync();
        }

        var ex = await Assert.ThrowsAsync<SWValidationException>(() =>
            new Resources.Xchanges.Retry(db, ctx, xs).Handle(xchange.Id, new XchangeRetry { Reset = false }));

        Assert.Contains("ALREADY_RETRIED", ex.Validations.Select(v => v.Key));
        await using var check = _fixture.CreateScope();
        Assert.Equal(0, await check.ServiceProvider.GetRequiredService<BitweenDbContext>()
            .Set<Xchange>().CountAsync(x => x.RetryFor == xchange.Id));
    }

    [Fact]
    public async Task BulkRetry_retries_a_chain_once_when_two_of_its_attempts_are_selected()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var ctx = scope.Superuser();

        var (_, xchange) = await FailedXchange(db, xs, "Bulk Dedupe Doc");
        var second = await RetryOnce(db, xs, ctx, xchange.Id);

        var plan = (XchangeBulkRetryPlan)await new Resources.Xchanges.BulkRetry(db, ctx, xs)
            .Handle(new XchangeBulkRetry { Ids = [xchange.Id, second.Id] });
        await db.SaveChangesAsync();

        Assert.Equal(2, plan.Selected);
        Assert.Equal(1, plan.WillRetry);
        Assert.Equal(1, await db.Set<Xchange>().CountAsync(x => x.RetryFor == second.Id));
    }

    /// <summary>
    /// Every selection in one chain has to resolve to the same end of it. The first version walked
    /// the tree a level at a time with a set of nodes already seen, which made a selection lagging
    /// behind another stop on a node the leader had stepped over - so it was reported as handing
    /// over to an attempt that had itself been retried, and retrying that threw ALREADY_RETRIED and
    /// took the whole selection down. Needs three or more attempts and two selections at different
    /// depths to show up at all.
    /// </summary>
    [Fact]
    public async Task BulkRetry_resolves_every_selection_in_one_chain_to_the_same_end()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var ctx = scope.Superuser();

        var (_, first) = await FailedXchange(db, xs, "Bulk Lagging Doc");
        var second = await RetryOnce(db, xs, ctx, first.Id);
        var third = await RetryOnce(db, xs, ctx, second.Id);
        var fourth = await RetryOnce(db, xs, ctx, third.Id);

        // The oldest and one in the middle: two selections, three and one attempts out of date.
        var plan = (XchangeBulkRetryPlan)await new Resources.Xchanges.BulkRetry(db, ctx, xs)
            .Handle(new XchangeBulkRetry { Ids = [first.Id, third.Id] });
        await db.SaveChangesAsync();

        Assert.Equal(2, plan.Selected);
        Assert.Equal(1, plan.WillRetry);
        Assert.All(plan.Substituted, sub => Assert.Equal(fourth.Id, sub.RetryId));
        Assert.Equal(2, plan.Substituted.Count);

        // And only the end of the chain grew.
        Assert.Equal(1, await db.Set<Xchange>().CountAsync(x => x.RetryFor == fourth.Id));
        foreach (var stale in new[] { first.Id, second.Id, third.Id })
            Assert.Equal(1, await db.Set<Xchange>().CountAsync(x => x.RetryFor == stale));
    }

    [Fact]
    public async Task BulkRetry_skips_a_chain_whose_newest_attempt_succeeded()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var ctx = scope.Superuser();

        var (_, xchange) = await FailedXchange(db, xs, "Bulk Succeeded Doc");

        await new Resources.Xchanges.Retry(db, ctx, xs).Handle(xchange.Id, new XchangeRetry());
        await db.SaveChangesAsync();
        var second = await db.Set<Xchange>().FirstAsync(x => x.RetryFor == xchange.Id);
        db.Set<XchangeResult>().Add(new XchangeResult(second.Id, null, null));
        await db.SaveChangesAsync();

        var plan = (XchangeBulkRetryPlan)await new Resources.Xchanges.BulkRetry(db, ctx, xs)
            .Handle(new XchangeBulkRetry { Ids = [xchange.Id] });
        await db.SaveChangesAsync();

        Assert.Equal(0, plan.WillRetry);
        Assert.Contains("succeeded", Assert.Single(plan.Skipped).Reason);
        Assert.False(await db.Set<Xchange>().AnyAsync(x => x.RetryFor == second.Id));
    }

    [Fact]
    public async Task BulkRetry_selects_every_exchange_a_filter_matches_minus_the_exclusions()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var ctx = scope.Superuser();

        // Three failures on one subscription - more than a page would show, in miniature.
        var (sub, first) = await FailedXchange(db, xs, "Bulk Filter Doc");
        var second = await xs.CreateXchange(sub, new XchangeFile("{}"));
        var third = await xs.CreateXchange(sub, new XchangeFile("{}"));
        await db.SaveChangesAsync();
        db.Set<XchangeResult>().Add(new XchangeResult(second.Id, null, null, exception: "boom"));
        db.Set<XchangeResult>().Add(new XchangeResult(third.Id, null, null, exception: "boom"));
        await db.SaveChangesAsync();

        var plan = (XchangeBulkRetryPlan)await new Resources.Xchanges.BulkRetry(db, ctx, xs)
            .Handle(new XchangeBulkRetry
            {
                // The same filter string the exchange list builds, so "select all matching" means
                // the set that was on screen.
                Filter = $"filter=SubscriptionId:1:{sub.Id}&filter=StatusFilter:1:3",
                ExcludeIds = [third.Id]
            });
        await db.SaveChangesAsync();

        Assert.Equal(2, plan.Selected);
        Assert.Equal(2, plan.WillRetry);
        Assert.True(await db.Set<Xchange>().AnyAsync(x => x.RetryFor == first.Id));
        Assert.True(await db.Set<Xchange>().AnyAsync(x => x.RetryFor == second.Id));
        Assert.False(await db.Set<Xchange>().AnyAsync(x => x.RetryFor == third.Id));
    }

    [Fact]
    public async Task BulkRetryPreview_describes_the_same_work_without_doing_it()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var ctx = scope.Superuser();

        var (_, xchange) = await FailedXchange(db, xs, "Preview Doc");
        var second = await RetryOnce(db, xs, ctx, xchange.Id);

        var plan = (XchangeBulkRetryPlan)await new Resources.Xchanges.BulkRetryPreview(db, ctx)
            .Handle(new XchangeBulkRetry { Ids = [xchange.Id] });

        Assert.Equal(1, plan.WillRetry);
        Assert.Equal(second.Id, Assert.Single(plan.Substituted).RetryId);
        Assert.False(await db.Set<Xchange>().AnyAsync(x => x.RetryFor == second.Id));
    }

    /// <summary>
    /// Retrying is an operator's action, and until this was added the endpoints took anyone's word
    /// for it: the UI hid the button without a permission, but the API accepted the call from any
    /// signed-in account. <c>PermissionGuardTests</c> could not have caught it - those exercise
    /// <c>EnsurePermission</c> itself, and the gap was in never calling it.
    /// </summary>
    [Fact]
    public async Task Retrying_is_refused_to_an_account_that_cannot_operate_exchanges()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

        var (_, xchange) = await FailedXchange(db, xs, "Retry Guard Perm Doc");

        var viewerId = await scope.AsNewViewer($"retry-guard-{System.Guid.NewGuid():N}");
        var viewer = scope.As(viewerId);

        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            new Resources.Xchanges.Retry(db, viewer, xs).Handle(xchange.Id, new XchangeRetry()));

        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            new Resources.Xchanges.BulkRetry(db, viewer, xs)
                .Handle(new XchangeBulkRetry { Ids = [xchange.Id] }));

        await Assert.ThrowsAsync<SWUnauthorizedException>(() =>
            new Resources.Xchanges.BulkRetryPreview(db, viewer)
                .Handle(new XchangeBulkRetry { Ids = [xchange.Id] }));

        Assert.False(await db.Set<Xchange>().AnyAsync(x => x.RetryFor == xchange.Id));
    }

    /// <summary>
    /// The plan has to answer the question actually being asked. Re-resolving properties needs the
    /// subscription, so for an exchange that has none the answer differs with the choice - and a
    /// plan that promised a retry the retry then skipped would make the confirmation worthless.
    /// </summary>
    [Fact]
    public async Task BulkRetryPreview_answers_for_the_reset_that_was_asked_about()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var ctx = scope.Superuser();

        // A document-only exchange: no subscription from the start, which is also what an exchange
        // whose subscription was later deleted looks like.
        var doc = new Document(null, "Preview Reset Doc", DocumentFormat.Json);
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var orphan = await xs.CreateXchange(doc, WorkGroup.None, new XchangeFile("{}"));
        await db.SaveChangesAsync();
        db.Set<XchangeResult>().Add(new XchangeResult(orphan.Id, null, null, exception: "boom"));
        await db.SaveChangesAsync();

        var preview = new Resources.Xchanges.BulkRetryPreview(db, ctx);

        var plain = (XchangeBulkRetryPlan)await preview.Handle(
            new XchangeBulkRetry { Ids = [orphan.Id], Reset = false });
        Assert.Equal(1, plain.WillRetry);
        Assert.Empty(plain.Skipped);

        var withReset = (XchangeBulkRetryPlan)await preview.Handle(
            new XchangeBulkRetry { Ids = [orphan.Id], Reset = true });
        Assert.Equal(0, withReset.WillRetry);
        Assert.Contains("subscription no longer exists", Assert.Single(withReset.Skipped).Reason);

        // And the retry itself agrees with the plan that described it.
        var done = (XchangeBulkRetryPlan)await new Resources.Xchanges.BulkRetry(db, ctx, xs)
            .Handle(new XchangeBulkRetry { Ids = [orphan.Id], Reset = true });
        await db.SaveChangesAsync();
        Assert.Equal(0, done.WillRetry);
        Assert.False(await db.Set<Xchange>().AnyAsync(x => x.RetryFor == orphan.Id));
    }

    /// <summary>
    /// A status the filter cannot read used to be dropped, which widened the selection to every
    /// exchange there is - harmless in a search, but bulk retry runs over whatever this selects.
    /// </summary>
    [Fact]
    public async Task BulkRetry_refuses_a_filter_carrying_an_unreadable_status()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var ctx = scope.Superuser();

        var (_, xchange) = await FailedXchange(db, xs, "Bad Status Doc");

        await Assert.ThrowsAsync<SWValidationException>(() =>
            new Resources.Xchanges.BulkRetry(db, ctx, xs)
                .Handle(new XchangeBulkRetry { Filter = "filter=StatusFilter:1:9" }));

        Assert.False(await db.Set<Xchange>().AnyAsync(x => x.RetryFor == xchange.Id));
    }

    // ─── What the dashboard reads ─────────────────────────────────────────────

    /// <summary>
    /// The dashboard's retry panel: which chains have been retried repeatedly and are still
    /// failing, and whether retrying is achieving anything.
    /// </summary>
    [Fact]
    public async Task RetrySummary_ranks_the_chains_that_keep_failing_and_says_whether_retries_work()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var ctx = scope.Superuser();

        // A chain of four failed attempts.
        var (_, deep) = await FailedXchange(db, xs, "Summary Deep Doc");
        var second = await RetryOnce(db, xs, ctx, deep.Id);
        var third = await RetryOnce(db, xs, ctx, second.Id);
        var fourth = await RetryOnce(db, xs, ctx, third.Id);

        // And one that was retried once, where the retry worked.
        var (_, healed) = await FailedXchange(db, xs, "Summary Healed Doc");
        await new Resources.Xchanges.Retry(db, ctx, xs).Handle(healed.Id, new XchangeRetry());
        await db.SaveChangesAsync();
        var healedRetry = await db.Set<Xchange>().FirstAsync(x => x.RetryFor == healed.Id);
        db.Set<XchangeResult>().Add(new XchangeResult(healedRetry.Id, null, null));
        await db.SaveChangesAsync();

        var summary = await new Resources.Dashboard.RetrySummary(db, ctx).Handle();
        var json = System.Text.Json.JsonSerializer.SerializeToElement(summary);

        // The still-failing chain is listed, at its true length. The one that came good is not:
        // its newest attempt succeeded, so there is nothing left to act on.
        var chains = json.GetProperty("FailingChains").EnumerateArray().ToList();
        var listed = chains.FirstOrDefault(c => c.GetProperty("Id").GetString() == fourth.Id);
        Assert.Equal(4, listed.GetProperty("Attempts").GetInt32());
        Assert.DoesNotContain(chains, c => c.GetProperty("Id").GetString() == healedRetry.Id);

        // Superseded attempts are not listed either - only where each chain got to.
        foreach (var stale in new[] { deep.Id, second.Id, third.Id })
            Assert.DoesNotContain(chains, c => c.GetProperty("Id").GetString() == stale);

        // And retries that worked are counted among the ones that finished.
        var retries = json.GetProperty("RetriesLast7Days");
        Assert.True(retries.GetProperty("Succeeded").GetInt32() >= 1);
        Assert.True(retries.GetProperty("Finished").GetInt32() >= retries.GetProperty("Succeeded").GetInt32());
    }

    // ─── Reading the chain back ───────────────────────────────────────────────

    [Fact]
    public async Task RetryTree_returns_the_whole_chain_whichever_attempt_is_asked_about()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var ctx = scope.Superuser();

        var (_, xchange) = await FailedXchange(db, xs, "Tree Doc");
        var second = await RetryOnce(db, xs, ctx, xchange.Id);
        var third = await RetryOnce(db, xs, ctx, second.Id);

        foreach (var asked in new[] { xchange.Id, second.Id, third.Id })
        {
            var tree = (XchangeRetryTree)await new Resources.Xchanges.RetryTree(db, ctx)
                .Handle(new XchangeRetryTreeRequest { Id = asked });

            Assert.Equal(xchange.Id, tree.RootId);
            Assert.False(tree.Truncated);
            Assert.Equal([xchange.Id, second.Id, third.Id], tree.Nodes.Select(n => n.Id));
            Assert.Equal([null, xchange.Id, second.Id], tree.Nodes.Select(n => n.RetryFor));
            Assert.All(tree.Nodes.Skip(1), n => Assert.True(n.ManualRetry));
        }
    }

    /// <summary>
    /// A chain is one piece of work, however many attempts it took. Asking for the newest attempt
    /// only is what turns a list of failures into a list of things still to deal with - and, used
    /// with a bulk retry, means the selection is already the attempts that can be retried.
    /// </summary>
    [Fact]
    public async Task Exchange_search_can_return_only_the_newest_attempt_of_each_chain()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var ctx = scope.Superuser();

        var (sub, first) = await FailedXchange(db, xs, "Latest Only Doc");
        var second = await RetryOnce(db, xs, ctx, first.Id);
        var third = await RetryOnce(db, xs, ctx, second.Id);

        // A second, unrelated failure on the same subscription that was never retried.
        var alone = await xs.CreateXchange(sub, new XchangeFile("{}"));
        await db.SaveChangesAsync();
        db.Set<XchangeResult>().Add(new XchangeResult(alone.Id, null, null, exception: "boom"));
        await db.SaveChangesAsync();

        var search = new Resources.Xchanges.Search(db, xs, ctx);

        var all = (SearchyResponse<XchangeRow>)await search.Handle(
            new SearchyRequest($"filter=SubscriptionId:1:{sub.Id}") { PageSize = 50 });
        Assert.Equal(4, all.Result.Count());

        var latest = (SearchyResponse<XchangeRow>)await search.Handle(
            new SearchyRequest($"filter=SubscriptionId:1:{sub.Id}&filter=LatestOnly:1:true") { PageSize = 50 });

        // The chain collapses to its own end, and the standalone failure is its own end too.
        var ids = latest.Result.Select(r => r.Id).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Contains(third.Id, ids);
        Assert.Contains(alone.Id, ids);
        Assert.Equal(2, latest.TotalCount);
        Assert.All(latest.Result, r => Assert.False(r.HasRetry));
    }

    [Fact]
    public async Task Exchange_search_reports_which_rows_have_been_retried()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();
        var ctx = scope.Superuser();

        var (sub, xchange) = await FailedXchange(db, xs, "HasRetry Doc");
        var second = await RetryOnce(db, xs, ctx, xchange.Id);

        var response = (SearchyResponse<XchangeRow>)await new Resources.Xchanges.Search(db, xs, ctx)
            .Handle(new SearchyRequest($"filter=SubscriptionId:1:{sub.Id}") { PageSize = 50 });

        Assert.True(response.Result.Single(r => r.Id == xchange.Id).HasRetry);
        Assert.False(response.Result.Single(r => r.Id == second.Id).HasRetry);
        Assert.Equal(xchange.Id, response.Result.Single(r => r.Id == second.Id).RetryFor);
    }
}
