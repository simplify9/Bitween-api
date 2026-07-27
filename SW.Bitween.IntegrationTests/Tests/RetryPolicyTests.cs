using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Resources.RetryPolicies;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

[Collection("Bitween")]
public class RetryPolicyTests
{
    private readonly BitweenFixture _fixture;

    public RetryPolicyTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static (Create create, Get get, Update update, Delete delete)
        Handlers(BitweenDbContext db, RequestContext ctx) => (
            new Create(db, ctx),
            new Get(db),
            new Update(db, ctx),
            new Delete(db, ctx));

    /// <summary>
    /// Handlers resolve their permissions from the database, and no account is signed in here.
    /// The break-glass superuser claim grants the whole catalog, so these tests exercise the
    /// handler rather than the guard in front of it.
    /// </summary>
    private static RequestContext Superuser(AsyncServiceScope scope)
    {
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();
        ctx.Set(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(Bitween.RequestContextExtensions.SuperuserClaim, "true")], "integration-test")));
        return ctx;
    }

    private static RetryPolicyCreate SimplePolicy(string name) => new()
    {
        Name = name,
        Groups =
        [
            new RetryGroup
            {
                Name = "Timeout",
                Priority = 10,
                AppliesTo = [XchangeResultType.Error],
                Matchers = [new ContainsMatcher { Value = "timeout" }],
                Budget = new RetryBudget
                {
                    MaxAttemptsPerError = 3,
                    MaxAttemptsTotal = 10,
                    DelayStrategy = new FixedDelayStrategy { DelayMs = 5_000 }
                }
            }
        ]
    };

    // ─── CRUD ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Can_create_and_get_retry_policy()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ctx = Superuser(scope);
        var (create, get, _, _) = Handlers(db, ctx);

        var id = (int)await create.Handle(SimplePolicy("Round-trip Policy"));

        var result = (RetryPolicyUpdate)await get.Handle(id);

        Assert.NotNull(result);
        Assert.Equal("Round-trip Policy", result.Name);
        Assert.Single(result.Groups);
        Assert.Equal("Timeout", result.Groups[0].Name);
    }

    [Fact]
    public async Task Create_policy_with_complex_groups_round_trips_json_correctly()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ctx = Superuser(scope);
        var (create, _, _, _) = Handlers(db, ctx);

        var policy = new RetryPolicyCreate
        {
            Name = "Complex JSON Policy",
            Groups =
            [
                new RetryGroup
                {
                    Name = "Exception Group",
                    Priority = 10,
                    AppliesTo = [XchangeResultType.Error],
                    Matchers = [new ExceptionTypeMatcher { Value = "System.TimeoutException" }],
                    Budget = new RetryBudget
                    {
                        MaxAttemptsPerError = 2,
                        MaxAttemptsTotal = 5,
                        DelayStrategy = new ExponentialDelayStrategy { InitialDelayMs = 1_000, Multiplier = 2, MaxDelayMs = 60_000 }
                    }
                },
                new RetryGroup
                {
                    Name = "Block Group",
                    Priority = 20,
                    AppliesTo = [XchangeResultType.BadResult],
                    Action = RetryAction.Block,
                    Matchers = [new JsonPathMatcher { Path = "$.error.code", Op = JsonPathOp.Eq, Value = "500" }]
                }
            ]
        };

        var id = (int)await create.Handle(policy);

        var reloaded = await db.Set<RetryPolicy>().AsNoTracking().SingleAsync(p => p.Id == id);

        Assert.Equal(2, reloaded.Groups.Count);

        var exGrp = reloaded.Groups.First(g => g.Name == "Exception Group");
        Assert.IsType<ExceptionTypeMatcher>(exGrp.Matchers[0]);
        Assert.IsType<ExponentialDelayStrategy>(exGrp.Budget!.DelayStrategy);

        var blockGrp = reloaded.Groups.First(g => g.Name == "Block Group");
        Assert.Equal(RetryAction.Block, blockGrp.Action);
        Assert.IsType<JsonPathMatcher>(blockGrp.Matchers[0]);
    }

    [Fact]
    public async Task Can_update_retry_policy_name_and_groups()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ctx = Superuser(scope);
        var (create, _, update, _) = Handlers(db, ctx);

        var id = (int)await create.Handle(SimplePolicy("Before Update"));

        await update.Handle(id, new RetryPolicyUpdate
        {
            Name = "After Update",
            Groups =
            [
                new RetryGroup
                {
                    Name = "New Group",
                    Priority = 5,
                    AppliesTo = [XchangeResultType.Error],
                    Matchers = [new RegexMatcher { Pattern = "connect" }],
                    Budget = new RetryBudget
                    {
                        MaxAttemptsPerError = 1,
                        MaxAttemptsTotal = 5,
                        DelayStrategy = new LinearDelayStrategy { InitialDelayMs = 1_000, IncrementMs = 500 }
                    }
                }
            ]
        });

        var reloaded = await db.Set<RetryPolicy>().AsNoTracking().SingleAsync(p => p.Id == id);

        Assert.Equal("After Update", reloaded.Name);
        Assert.Single(reloaded.Groups);
        Assert.Equal("New Group", reloaded.Groups[0].Name);
        Assert.IsType<LinearDelayStrategy>(reloaded.Groups[0].Budget!.DelayStrategy);
    }

    [Fact]
    public async Task Can_delete_retry_policy_not_assigned_to_any_subscription()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ctx = Superuser(scope);
        var (create, _, _, delete) = Handlers(db, ctx);

        var id = (int)await create.Handle(SimplePolicy("Deletable Policy"));

        await delete.Handle(id);

        var exists = await db.Set<RetryPolicy>().AnyAsync(p => p.Id == id);
        Assert.False(exists);
    }

    // ─── Delete guard ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Cannot_delete_retry_policy_that_is_assigned_to_a_subscription()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ctx = Superuser(scope);
        var (create, _, _, delete) = Handlers(db, ctx);

        var doc = new Document(7001, "Delete Guard Doc");
        db.Set<Document>().Add(doc);
        var sub = new Subscription("Delete Guard Sub", doc.Id);
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();

        var policyId = (int)await create.Handle(SimplePolicy("In-Use Policy"));

        sub.SetRetryPolicy(policyId, null);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<SWException>(() => delete.Handle(policyId));

        // Policy must still exist after the blocked delete
        var stillExists = await db.Set<RetryPolicy>().AnyAsync(p => p.Id == policyId);
        Assert.True(stillExists);
    }

    // ─── Payload validation ───────────────────────────────────────────────────

    [Fact]
    public async Task Creating_policy_with_null_name_violates_not_null_db_constraint()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        db.Set<RetryPolicy>().Add(new RetryPolicy { Name = null!, Groups = [] });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Creating_policy_with_name_over_200_chars_violates_db_constraint()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        db.Set<RetryPolicy>().Add(new RetryPolicy { Name = new string('X', 201), Groups = [] });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ─── Subscription retry fields ────────────────────────────────────────────

    [Fact]
    public async Task Subscription_retry_policy_id_is_persisted_and_fk_resolves()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ctx = Superuser(scope);
        var (create, _, _, _) = Handlers(db, ctx);

        var doc = new Document(7002, "Sub FK Doc");
        db.Set<Document>().Add(doc);
        var sub = new Subscription("Sub with Policy", doc.Id);
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();

        var policyId = (int)await create.Handle(SimplePolicy("FK Test Policy"));

        // Mirror what Subscriptions/Update.cs does: set the FK field and save
        sub.SetRetryPolicy(policyId, null);
        await db.SaveChangesAsync();

        var reloaded = await db.Set<Subscription>()
            .Include(s => s.RetryPolicy)
            .AsNoTracking()
            .SingleAsync(s => s.Id == sub.Id);

        Assert.Equal(policyId, reloaded.RetryPolicyId);
        Assert.NotNull(reloaded.RetryPolicy);
        Assert.Equal("FK Test Policy", reloaded.RetryPolicy.Name);
    }

    [Fact]
    public async Task Subscription_custom_retry_policy_json_is_persisted_and_reloads_with_polymorphic_types()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var doc = new Document(7003, "Sub Custom Policy Doc");
        db.Set<Document>().Add(doc);
        var sub = new Subscription("Sub with Custom Policy", doc.Id);
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();

        // Mirror what Subscriptions/Update.cs does: set the inline policy and save
        sub.SetRetryPolicy(null, new CustomRetryPolicy
        {
            Groups =
            [
                new RetryGroup
                {
                    Name = "Custom Timeout",
                    Priority = 10,
                    AppliesTo = [XchangeResultType.Error],
                    Matchers = [new ContainsMatcher { Value = "timeout" }],
                    Budget = new RetryBudget
                    {
                        MaxAttemptsPerError = 3,
                        MaxAttemptsTotal = 10,
                        DelayStrategy = new LinearDelayStrategy { InitialDelayMs = 1_000, IncrementMs = 500 }
                    }
                }
            ]
        });
        await db.SaveChangesAsync();

        var reloaded = await db.Set<Subscription>().AsNoTracking().SingleAsync(s => s.Id == sub.Id);

        Assert.NotNull(reloaded.CustomRetryPolicy);
        Assert.Single(reloaded.CustomRetryPolicy.Groups);
        Assert.Equal("Custom Timeout", reloaded.CustomRetryPolicy.Groups[0].Name);
        Assert.IsType<ContainsMatcher>(reloaded.CustomRetryPolicy.Groups[0].Matchers[0]);
        Assert.IsType<LinearDelayStrategy>(reloaded.CustomRetryPolicy.Groups[0].Budget!.DelayStrategy);
    }

    [Fact]
    public async Task Removing_retry_policy_nullifies_subscription_fk_via_set_null_cascade()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var doc = new Document(7004, "Sub SetNull Doc");
        db.Set<Document>().Add(doc);
        var sub = new Subscription("Sub SetNull Test", doc.Id);
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();

        var policy = new RetryPolicy { Name = "SetNull Policy", Groups = [] };
        db.Set<RetryPolicy>().Add(policy);
        await db.SaveChangesAsync();

        sub.SetRetryPolicy(policy.Id, null);
        await db.SaveChangesAsync();

        // Remove the policy directly (bypassing the handler's guard) to test the ON DELETE SET NULL cascade
        db.Set<RetryPolicy>().Remove(policy);
        await db.SaveChangesAsync();

        var reloaded = await db.Set<Subscription>().AsNoTracking().SingleAsync(s => s.Id == sub.Id);
        Assert.Null(reloaded.RetryPolicyId);
    }

    // ─── Test / dry-run endpoint ────────────────────────────────────────────────

    [Fact]
    public async Task Test_simulates_consecutive_attempts_and_stops_once_blocked()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ctx = Superuser(scope);
        var handler = new Resources.RetryPolicies.Test(db, ctx);

        var request = new TestRetryPolicyRequest
        {
            Groups = SimplePolicy("Dry-run Policy").Groups,
            ResultType = XchangeResultType.Error,
            Content = "System.TimeoutException: contains timeout",
            AttemptsToSimulate = 5
        };

        var response = (TestRetryPolicyResponse)await handler.Handle(request);

        // Budget is MaxAttemptsPerError = 3, so attempts 1-3 retry and attempt 4 is blocked;
        // simulation stops there rather than continuing to the requested 5.
        Assert.Equal(4, response.Attempts.Count);
        Assert.All(response.Attempts.Take(3), a =>
        {
            Assert.True(a.ShouldRetry);
            Assert.Equal("Timeout", a.MatchedGroupName);
            Assert.Equal(5, a.DelaySeconds);
        });
        Assert.False(response.Attempts[3].ShouldRetry);
        Assert.Null(response.Attempts[3].MatchedGroupName);
    }

    [Fact]
    public async Task Test_rejects_success_result_type()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ctx = Superuser(scope);
        var handler = new Resources.RetryPolicies.Test(db, ctx);

        var request = new TestRetryPolicyRequest
        {
            Groups = [],
            ResultType = XchangeResultType.Success,
            Content = "n/a"
        };

        await Assert.ThrowsAsync<SWValidationException>(() => handler.Handle(request));
    }

    [Fact]
    public async Task Test_reports_no_match_when_no_group_applies()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ctx = Superuser(scope);
        var handler = new Resources.RetryPolicies.Test(db, ctx);

        var request = new TestRetryPolicyRequest
        {
            Groups = SimplePolicy("Dry-run Policy").Groups,
            ResultType = XchangeResultType.Error,
            Content = "System.NullReferenceException: unrelated failure",
            AttemptsToSimulate = 3
        };

        var response = (TestRetryPolicyResponse)await handler.Handle(request);

        Assert.Single(response.Attempts);
        Assert.False(response.Attempts[0].ShouldRetry);
        Assert.Null(response.Attempts[0].MatchedGroupName);
    }

}
