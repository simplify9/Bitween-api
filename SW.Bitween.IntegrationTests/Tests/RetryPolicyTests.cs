using System;
using System.Collections.Generic;
using System.Linq;
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
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();
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
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();
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
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();
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
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();
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
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();
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
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();
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

    // ─── Shared group total (MaxAttemptsTotal) ──────────────────────────────────

    [Fact]
    public async Task Group_total_is_shared_across_separate_messages_of_the_same_integration()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var doc = new Document(7005, "Shared Total Doc");
        db.Set<Document>().Add(doc);
        var sub = new Subscription("Shared Total Sub", doc.Id);
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();

        var group = new RetryGroup
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
        };
        var policy = new CustomRetryPolicy { Groups = [group] };

        // Reproduces the reported bug: four failing messages, each retried up to its own
        // per-message cap of 3, under a shared total of 10 — 12 retries before the fix.
        var allowed = 0;
        for (var message = 0; message < 4; message++)
        for (var attempt = 0; attempt < 3; attempt++)
        {
            // A fresh evaluator and store per failure, exactly as XchangeService builds them.
            var evaluator = new RetryPolicyEvaluator(policy, new RetryGroupBudget(db, scope.ServiceProvider, sub.Id));
            var decision = await evaluator.Evaluate(XchangeResultType.Error, "timeout", attempt);
            if (decision.ShouldRetry) allowed++;
            await db.SaveChangesAsync();
        }

        Assert.Equal(10, allowed);

        var usage = await db.Set<RetryGroupUsage>().AsNoTracking()
            .SingleAsync(u => u.SubscriptionId == sub.Id && u.GroupId == group.Id);
        Assert.Equal(10, usage.AttemptsUsed);
    }

    [Fact]
    public async Task Group_total_is_tracked_per_integration_not_per_policy()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var doc = new Document(7006, "Per Integration Doc");
        db.Set<Document>().Add(doc);
        var subA = new Subscription("Per Integration Sub A", doc.Id);
        var subB = new Subscription("Per Integration Sub B", doc.Id);
        db.Set<Subscription>().AddRange(subA, subB);
        await db.SaveChangesAsync();

        var group = new RetryGroup
        {
            Name = "Timeout",
            Priority = 10,
            AppliesTo = [XchangeResultType.Error],
            Matchers = [new ContainsMatcher { Value = "timeout" }],
            Budget = new RetryBudget
            {
                MaxAttemptsPerError = 10,
                MaxAttemptsTotal = 1,
                DelayStrategy = new FixedDelayStrategy { DelayMs = 5_000 }
            }
        };
        var policy = new CustomRetryPolicy { Groups = [group] };

        // Each integration gets its own single attempt, so one integration exhausting a
        // shared policy template cannot starve the others.
        Assert.True(await Allow(subA.Id));
        Assert.True(await Allow(subB.Id));
        Assert.False(await Allow(subA.Id));
        Assert.False(await Allow(subB.Id));
        return;

        async Task<bool> Allow(int subscriptionId)
        {
            var evaluator = new RetryPolicyEvaluator(policy, new RetryGroupBudget(db, scope.ServiceProvider, subscriptionId));
            var decision = await evaluator.Evaluate(XchangeResultType.Error, "timeout", 0);
            await db.SaveChangesAsync();
            return decision.ShouldRetry;
        }
    }

    [Fact]
    public async Task Concurrent_claims_never_exceed_the_group_total()
    {
        await using var setup = _fixture.CreateScope();
        var setupDb = setup.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var doc = new Document(7010, "Concurrent Budget Doc");
        setupDb.Set<Document>().Add(doc);
        var sub = new Subscription("Concurrent Budget Sub", doc.Id);
        setupDb.Set<Subscription>().Add(sub);
        await setupDb.SaveChangesAsync();

        var groupId = Guid.NewGuid();
        const int cap = 5;
        const int racers = 16;

        // Bitween runs several instances, so simultaneous failures of the same integration and
        // group are normal. Each racer gets its own scope and context, mimicking separate
        // instances: a read-then-write would let several observe the same free slot at once.
        var tasks = Enumerable.Range(0, racers).Select(async _ =>
        {
            await using var scope = _fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            return await new RetryGroupBudget(db, scope.ServiceProvider, sub.Id).TryConsume(groupId, cap);
        });

        var claims = await Task.WhenAll(tasks);
        var granted = claims.Count(claim => claim.Granted);

        Assert.Equal(cap, granted);

        var usage = await setupDb.Set<RetryGroupUsage>().AsNoTracking()
            .SingleAsync(u => u.SubscriptionId == sub.Id && u.GroupId == groupId);
        Assert.Equal(cap, usage.AttemptsUsed);
    }

    // ─── Usage reporting and reset ──────────────────────────────────────────────

    [Fact]
    public async Task Usage_reports_spent_budget_and_reset_clears_it()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();

        var doc = new Document(7007, "Usage Doc");
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var policyId = (int)await new Create(db, ctx).Handle(SimplePolicy("Usage Policy"));
        var saved = await db.Set<RetryPolicy>().AsNoTracking().SingleAsync(p => p.Id == policyId);
        var groupId = saved.Groups[0].Id;

        var sub = new Subscription("Usage Sub", doc.Id);
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();
        sub.SetRetryPolicy(policyId, null);
        await db.SaveChangesAsync();

        // Spend the whole budget (SimplePolicy allows 10 in total).
        var budget = new RetryGroupBudget(db, scope.ServiceProvider, sub.Id);
        for (var i = 0; i < 10; i++) await budget.TryConsume(groupId, 10);
        await db.SaveChangesAsync();

        var rows = (List<RetryGroupUsageRow>)await new Usage(db, ctx).Handle(policyId, new RetryPolicyUsageRequest());
        var row = Assert.Single(rows);
        Assert.Equal(sub.Id, row.SubscriptionId);
        Assert.Equal("Usage Sub", row.SubscriptionName);
        Assert.Equal("Timeout", row.GroupName);
        Assert.Equal(10, row.AttemptsUsed);
        Assert.True(row.Exhausted);

        await new ResetUsage(db, ctx).Handle(policyId, new RetryPolicyResetUsage
        {
            SubscriptionId = sub.Id,
            GroupId = groupId
        });

        // The pair keeps its row — every subscription-and-group pair gets one so an alert override
        // stays configurable before the first failure — but with nothing spent against the ceiling.
        var afterReset = Assert.Single(
            (List<RetryGroupUsageRow>)await new Usage(db, ctx).Handle(policyId, new RetryPolicyUsageRequest()));
        Assert.Equal(0, afterReset.AttemptsUsed);
        Assert.False(afterReset.Exhausted);
        Assert.Null(afterReset.LastAttemptOn);

        // And the group can retry again.
        Assert.True((await new RetryGroupBudget(db, scope.ServiceProvider, sub.Id).TryConsume(groupId, 10)).Granted);
    }

    [Fact]
    public async Task Usage_lists_never_failed_pairs_and_skips_groups_that_cannot_exhaust()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();

        var doc = new Document(7011, "Never Failed Doc");
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var model = SimplePolicy("Never Failed Policy");
        model.AlertHandlerId = "NativeSmtpHandler";

        // A Block group carries no budget, and the evaluator refuses before it ever claims one, so
        // it can never exhaust and never alert. Reporting it would invite configuring an alert that
        // cannot fire.
        model.Groups.Add(new RetryGroup
        {
            Name = "Never retry",
            Priority = 20,
            Action = RetryAction.Block,
            AppliesTo = [XchangeResultType.Error],
            Matchers = [new ContainsMatcher { Value = "fatal" }]
        });

        var policyId = (int)await new Create(db, ctx).Handle(model);

        var sub = new Subscription("Never Failed Sub", doc.Id);
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();
        sub.SetRetryPolicy(policyId, null);
        await db.SaveChangesAsync();

        var rows = (List<RetryGroupUsageRow>)await new Usage(db, ctx)
            .Handle(policyId, new RetryPolicyUsageRequest());

        // One row, not two: the pair is reported even though nothing has ever failed — otherwise its
        // alert override would be unreachable until after the first failure — while the Block group
        // is left out entirely.
        var row = Assert.Single(rows);
        Assert.Equal("Timeout", row.GroupName);
        Assert.Equal(0, row.AttemptsUsed);
        Assert.Equal(10, row.MaxAttemptsTotal);
        Assert.False(row.Exhausted);
        Assert.Null(row.LastAttemptOn);
        Assert.Equal("NativeSmtpHandler", row.ResolvedHandlerId);
        Assert.Equal(RetryAlertLevel.Policy, row.ResolvedFrom);
    }

    [Fact]
    public async Task Reset_does_not_touch_counters_of_another_policy()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();

        var doc = new Document(7008, "Reset Scope Doc");
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var mineId = (int)await new Create(db, ctx).Handle(SimplePolicy("Reset Scope Mine"));
        var otherId = (int)await new Create(db, ctx).Handle(SimplePolicy("Reset Scope Other"));
        var otherGroupId = (await db.Set<RetryPolicy>().AsNoTracking()
            .SingleAsync(p => p.Id == otherId)).Groups[0].Id;

        var otherSub = new Subscription("Reset Scope Other Sub", doc.Id);
        db.Set<Subscription>().Add(otherSub);
        await db.SaveChangesAsync();
        otherSub.SetRetryPolicy(otherId, null);
        await db.SaveChangesAsync();

        await new RetryGroupBudget(db, scope.ServiceProvider, otherSub.Id).TryConsume(otherGroupId, 10);
        await db.SaveChangesAsync();

        // Resetting everything under one policy must leave the other policy's counters alone.
        await new ResetUsage(db, ctx).Handle(mineId, new RetryPolicyResetUsage());

        // A row now exists for every pair whether or not it has failed, so assert the spent counter
        // itself survived — row count alone would pass even if the reset had wrongly cleared it.
        var otherRow = Assert.Single(
            (List<RetryGroupUsageRow>)await new Usage(db, ctx).Handle(otherId, new RetryPolicyUsageRequest()));
        Assert.Equal(1, otherRow.AttemptsUsed);
    }

    [Fact]
    public async Task Removing_a_group_clears_its_spent_budget()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();

        var doc = new Document(7009, "Removed Group Doc");
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var policyId = (int)await new Create(db, ctx).Handle(SimplePolicy("Removed Group Policy"));
        var saved = await db.Set<RetryPolicy>().AsNoTracking().SingleAsync(p => p.Id == policyId);
        var groupId = saved.Groups[0].Id;

        var sub = new Subscription("Removed Group Sub", doc.Id);
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();
        sub.SetRetryPolicy(policyId, null);
        await db.SaveChangesAsync();

        await new RetryGroupBudget(db, scope.ServiceProvider, sub.Id).TryConsume(groupId, 10);
        await db.SaveChangesAsync();
        Assert.True(await db.Set<RetryGroupUsage>().AnyAsync(u => u.GroupId == groupId));

        // Dropping the group must take its counter with it, or the row is stranded where
        // neither the usage report nor reset can reach it.
        await new Update(db, ctx).Handle(policyId, new RetryPolicyUpdate
        {
            Name = "Removed Group Policy",
            Groups = []
        });

        Assert.False(await db.Set<RetryGroupUsage>().AnyAsync(u => u.GroupId == groupId));
    }

    // ─── Attempts drill-down ────────────────────────────────────────────────────

    [Fact]
    public async Task Attempts_lists_only_this_pairs_stamped_failures_pending_first()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();

        var doc = new Document(7012, "Attempts Doc");
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var policyId = (int)await new Create(db, ctx).Handle(SimplePolicy("Attempts Policy"));
        var groupId = (await db.Set<RetryPolicy>().AsNoTracking()
            .SingleAsync(p => p.Id == policyId)).Groups[0].Id;

        var sub = new Subscription("Attempts Sub", doc.Id);
        var otherSub = new Subscription("Attempts Other Sub", doc.Id);
        db.Set<Subscription>().AddRange(sub, otherSub);
        await db.SaveChangesAsync();
        sub.SetRetryPolicy(policyId, null);
        otherSub.SetRetryPolicy(policyId, null);
        await db.SaveChangesAsync();

        // Still being worked on: a scheduled retry is outstanding for it.
        var pending = new Xchange(sub, new XchangeFile("{}"));
        var pendingResult = new XchangeResult(pending.Id, null, null, exception: "first timeout");
        pendingResult.SetRetryEvaluation(groupId, 0);

        // Given up on, and the reason recorded.
        var stopped = new Xchange(sub, new XchangeFile("{}"));
        var stoppedResult = new XchangeResult(stopped.Id, null, null, exception: "second timeout");
        stoppedResult.SetRetryEvaluation(groupId, 1);
        stoppedResult.SetRetryBlocked("Group 'Timeout' has used all 10 of its total attempts");

        // Carries no group: this is what every failure recorded before the group was stamped onto
        // results looks like, and it has no pair to be listed under.
        var unstamped = new Xchange(sub, new XchangeFile("{}"));
        var unstampedResult = new XchangeResult(unstamped.Id, null, null, exception: "older timeout");

        // Same policy and same group, different subscription — a row of its own, not this one's.
        var otherPair = new Xchange(otherSub, new XchangeFile("{}"));
        var otherPairResult = new XchangeResult(otherPair.Id, null, null, exception: "someone else's timeout");
        otherPairResult.SetRetryEvaluation(groupId, 0);

        db.Set<Xchange>().AddRange(pending, stopped, unstamped, otherPair);
        db.Set<XchangeResult>().AddRange(pendingResult, stoppedResult, unstampedResult, otherPairResult);
        db.Set<DelayedRetry>().Add(new DelayedRetry { Id = pending.Id, On = DateTime.UtcNow.AddMinutes(5) });
        await db.SaveChangesAsync();

        var result = (RetryGroupAttempts)await new Attempts(db, ctx).Handle(policyId,
            new RetryGroupAttemptsRequest { SubscriptionId = sub.Id, GroupId = groupId });

        // Two, not four: the unstamped failure and the other subscription's are both out.
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Attempts.Count);

        // Pending leads, so a long history of finished failures can never push the one still moving
        // out of a capped list.
        Assert.Equal(pending.Id, result.Attempts[0].XchangeId);
        Assert.True(result.Attempts[0].RetryPending);
        Assert.Equal(0, result.Attempts[0].AttemptNumber);
        Assert.Equal("first timeout", result.Attempts[0].Exception);
        Assert.Null(result.Attempts[0].RetryBlockedReason);

        Assert.Equal(stopped.Id, result.Attempts[1].XchangeId);
        Assert.False(result.Attempts[1].RetryPending);
        Assert.Equal(1, result.Attempts[1].AttemptNumber);
        Assert.Contains("used all 10", result.Attempts[1].RetryBlockedReason);
    }

    [Fact]
    public async Task Attempts_rejects_a_subscription_that_does_not_use_the_policy()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();

        var doc = new Document(7013, "Attempts Scope Doc");
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var mineId = (int)await new Create(db, ctx).Handle(SimplePolicy("Attempts Scope Mine"));
        var theirsId = (int)await new Create(db, ctx).Handle(SimplePolicy("Attempts Scope Theirs"));
        var theirGroupId = (await db.Set<RetryPolicy>().AsNoTracking()
            .SingleAsync(p => p.Id == theirsId)).Groups[0].Id;

        var theirSub = new Subscription("Attempts Scope Their Sub", doc.Id);
        db.Set<Subscription>().Add(theirSub);
        await db.SaveChangesAsync();
        theirSub.SetRetryPolicy(theirsId, null);
        await db.SaveChangesAsync();

        // Asking one policy for another policy's subscription must fail rather than quietly answer:
        // the route key is what the caller was authorised against.
        await Assert.ThrowsAsync<SWNotFoundException>(() => new Attempts(db, ctx).Handle(mineId,
            new RetryGroupAttemptsRequest { SubscriptionId = theirSub.Id, GroupId = theirGroupId }));
    }

    // ─── Test / dry-run endpoint ────────────────────────────────────────────────

    [Fact]
    public async Task Test_simulates_consecutive_attempts_and_stops_once_blocked()
    {
        await using var scope = _fixture.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();
        var handler = new Resources.RetryPolicies.Test(ctx);

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
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();
        var handler = new Resources.RetryPolicies.Test(ctx);

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
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();
        var handler = new Resources.RetryPolicies.Test(ctx);

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

    // ─── Exhaustion alert claim ─────────────────────────────────────────────────

    [Fact]
    public async Task Exhausting_a_budget_claims_the_alert_exactly_once()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var doc = new Document(7101, "Alert Claim Doc");
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var sub = new Subscription("Alert Claim Sub", doc.Id);
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();

        var groupId = Guid.NewGuid();
        var budget = new RetryGroupBudget(db, scope.ServiceProvider, sub.Id);

        // Spending the budget never alerts — nothing has been refused yet.
        for (var i = 0; i < 3; i++)
        {
            var spending = await budget.TryConsume(groupId, 3);
            Assert.True(spending.Granted);
            Assert.False(spending.JustExhausted);
        }

        // The first refusal owns the alert.
        var first = await budget.TryConsume(groupId, 3);
        Assert.False(first.Granted);
        Assert.True(first.JustExhausted);

        // Every refusal after it stays quiet, however many failures arrive.
        var second = await budget.TryConsume(groupId, 3);
        Assert.False(second.Granted);
        Assert.False(second.JustExhausted);

        var usage = await db.Set<RetryGroupUsage>().AsNoTracking()
            .SingleAsync(u => u.SubscriptionId == sub.Id && u.GroupId == groupId);
        Assert.NotNull(usage.ExhaustedNotifiedOn);
    }

    [Fact]
    public async Task Concurrent_refusals_claim_the_alert_only_once()
    {
        await using var setupScope = _fixture.CreateScope();
        var setupDb = setupScope.ServiceProvider.GetRequiredService<BitweenDbContext>();

        var doc = new Document(7102, "Alert Race Doc");
        setupDb.Set<Document>().Add(doc);
        await setupDb.SaveChangesAsync();

        var sub = new Subscription("Alert Race Sub", doc.Id);
        setupDb.Set<Subscription>().Add(sub);
        await setupDb.SaveChangesAsync();

        var groupId = Guid.NewGuid();

        // Spends the only attempt, so every racer below meets an empty budget. Asserted, or a
        // failure here would surface as a confusing claim count further down.
        var setupClaim = await new RetryGroupBudget(setupDb, setupScope.ServiceProvider, sub.Id)
            .TryConsume(groupId, 1);
        Assert.True(setupClaim.Granted);

        // Several instances can discover the empty budget in the same instant; a read-then-write
        // would let each of them decide it was the first and send its own email.
        var tasks = Enumerable.Range(0, 12).Select(async _ =>
        {
            await using var scope = _fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            return await new RetryGroupBudget(db, scope.ServiceProvider, sub.Id).TryConsume(groupId, 1);
        });

        var claims = await Task.WhenAll(tasks);

        Assert.Equal(1, claims.Count(c => c.JustExhausted));
        Assert.DoesNotContain(claims, c => c.Granted);
    }

    [Fact]
    public async Task Resetting_usage_re_arms_the_exhaustion_alert()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();

        var doc = new Document(7103, "Alert Rearm Doc");
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var policyId = (int)await new Create(db, ctx).Handle(SimplePolicy("Alert Rearm Policy"));
        var saved = await db.Set<RetryPolicy>().AsNoTracking().SingleAsync(p => p.Id == policyId);
        var groupId = saved.Groups[0].Id;

        var sub = new Subscription("Alert Rearm Sub", doc.Id);
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();
        sub.SetRetryPolicy(policyId, null);
        await db.SaveChangesAsync();

        var budget = new RetryGroupBudget(db, scope.ServiceProvider, sub.Id);
        for (var i = 0; i < 10; i++) await budget.TryConsume(groupId, 10);
        Assert.True((await budget.TryConsume(groupId, 10)).JustExhausted);

        await new ResetUsage(db, ctx).Handle(policyId, new RetryPolicyResetUsage
        {
            SubscriptionId = sub.Id,
            GroupId = groupId
        });

        // Reset deletes the row, so the budget and its alert come back together.
        for (var i = 0; i < 10; i++) await budget.TryConsume(groupId, 10);
        Assert.True((await budget.TryConsume(groupId, 10)).JustExhausted);
    }

    // ─── Alert config validation ───────────────────────────────────────────────

    [Fact]
    public async Task Cannot_save_a_group_that_sends_its_own_alert_without_a_handler()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();

        var model = SimplePolicy("Alert Validation Policy");
        model.Groups =
        [
            new RetryGroup
            {
                Name = model.Groups[0].Name,
                Priority = model.Groups[0].Priority,
                AppliesTo = model.Groups[0].AppliesTo,
                Matchers = model.Groups[0].Matchers,
                Budget = model.Groups[0].Budget,
                AlertMode = RetryAlertMode.Send
            }
        ];

        await Assert.ThrowsAsync<SWValidationException>(() => new Create(db, ctx).Handle(model));
    }

    [Fact]
    public async Task Policy_alert_handler_round_trips()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var ctx = scope.ServiceProvider.GetRequiredService<RequestContext>();

        var model = SimplePolicy("Alert Handler Policy");
        model.AlertHandlerId = "NativeSmtpHandler";
        model.AlertHandlerProperties = new Dictionary<string, string> { ["to"] = "ops@example.com" };

        var policyId = (int)await new Create(db, ctx).Handle(model);
        var loaded = (RetryPolicyUpdate)await new Get(db).Handle(policyId);

        Assert.Equal("NativeSmtpHandler", loaded.AlertHandlerId);
        Assert.Equal("ops@example.com", loaded.AlertHandlerProperties["to"]);
    }

}
