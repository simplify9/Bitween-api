using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SW.Bitween.Domain;
using SW.Bitween.IntegrationTests.Fixtures;
using SW.Bitween.Model;
using SW.Bitween.Model.AutoRetry;
using SW.Bitween.Services;
using SW.PrimitiveTypes;
using Xunit;

namespace SW.Bitween.IntegrationTests.Tests;

/// <summary>
/// An adapter saying when to try again: the retry its policy allows waits at least that long, up to
/// the cap, and nothing else about the policy changes.
/// </summary>
[Collection("Bitween")]
public class RetryAfterTests(BitweenFixture fixture)
{
    static int _seq;

    static CustomRetryPolicy Policy(string matching = null) => new()
    {
        Groups =
        [
            new RetryGroup
            {
                Name = "Errors",
                Priority = 10,
                AppliesTo = [XchangeResultType.Error],
                Matchers = matching == null ? [] : [new ContainsMatcher { Value = matching }],
                Budget = new RetryBudget
                {
                    MaxAttemptsPerError = 5,
                    MaxAttemptsTotal = 1000,
                    DelayStrategy = new FixedDelayStrategy { DelayMs = 2_000 },
                },
            },
        ],
    };

    /// <summary>Fails an exchange with <paramref name="error"/> and returns when its retry was scheduled for, if at all.</summary>
    async Task<(TimeSpan? Wait, string Blocked)> FailWith(string error, CustomRetryPolicy policy = null, int? cap = null)
    {
        var options = fixture.App.Services.GetRequiredService<BitweenOptions>();
        var before = options.MaxRetryAfterSeconds;
        if (cap != null) options.MaxRetryAfterSeconds = cap.Value;
        try
        {
            await using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
            var xs = scope.ServiceProvider.GetRequiredService<XchangeService>();

            var name = $"Retry after {System.Threading.Interlocked.Increment(ref _seq)} {Guid.NewGuid():N}";
            var doc = new Document(null, name, DocumentFormat.Json);
            db.Add(doc);
            await db.SaveChangesAsync();
            var sub = new Subscription(name, doc.Id) { Inactive = false };
            sub.SetRetryPolicy(null, policy ?? Policy());
            db.Add(sub);
            await db.SaveChangesAsync();
            var xchange = await xs.CreateXchange(sub, new XchangeFile("{}"));
            await db.SaveChangesAsync();

            var result = new XchangeResult(xchange.Id, null, null, exception: error);
            db.Add(result);
            var started = DateTime.UtcNow;
            await xs.TryScheduleAutoRetry(xchange, XchangeResultType.Error, error, result);
            await db.SaveChangesAsync();

            var retry = await db.Set<DelayedRetry>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == xchange.Id);
            return (retry == null ? null : retry.On - started, result.RetryBlockedReason);
        }
        finally
        {
            options.MaxRetryAfterSeconds = before;
        }
    }

    static string Asking(int seconds, string type = "System.Exception") =>
        $"{type}: {{{{retryAfter:{seconds}}}}} The partner answered 429 and asked to wait.\n   at Somewhere()";

    [Fact]
    public async Task Without_a_hint_the_policy_s_delay_is_used_as_before()
    {
        var (wait, _) = await FailWith("System.Exception: 503: ServiceUnavailable: down");
        Assert.InRange(wait!.Value.TotalSeconds, 1.5, 2.6);
    }

    [Fact]
    public async Task A_hint_longer_than_the_policy_s_delay_is_waited_out()
    {
        var (wait, _) = await FailWith(Asking(120));
        Assert.InRange(wait!.Value.TotalSeconds, 119, 133);
    }

    [Fact]
    public async Task A_hint_shorter_than_the_policy_s_delay_doesn_t_make_the_retry_sooner()
    {
        var (wait, _) = await FailWith(Asking(0));
        Assert.InRange(wait!.Value.TotalSeconds, 1.5, 2.6);
    }

    [Fact]
    public async Task A_hint_beyond_the_cap_waits_only_the_cap_and_a_cap_of_0_ignores_hints()
    {
        var (capped, _) = await FailWith(Asking(999_999), cap: 300);
        Assert.InRange(capped!.Value.TotalSeconds, 299, 331);

        var (ignored, _) = await FailWith(Asking(120), cap: 0);
        Assert.InRange(ignored!.Value.TotalSeconds, 1.5, 2.6);
    }

    [Fact]
    public async Task A_hint_never_makes_a_retry_the_policy_wouldn_t()
    {
        var (wait, blocked) = await FailWith(Asking(60), Policy(matching: "timeout"));
        Assert.Null(wait);
        Assert.NotNull(blocked);
    }

    [Theory]
    [InlineData("System.Exception: {{error}}SW.Serverless.Sdk.RetryLaterException: {{retryAfter:90}} wait{{newline}}   at X")]
    [InlineData("SW.Serverless.Resident.AdapterInvocationException: RetryLaterException: {{retryAfter:90}} wait")]
    [InlineData("SW.Serverless.Resident.AdapterInvocationException: Error: {{retryAfter:90}} wait")]
    [InlineData("System.Exception: 429: TooManyRequests: slow Retry-After: 90s.\n ---> SW.Bitween.NativeAdapters.RetryLaterException: {{retryAfter:90}} The partner answered 429 and asked to wait.")]
    public async Task Every_runtime_s_way_of_reporting_it_is_read(string error)
    {
        var (wait, _) = await FailWith(error);
        Assert.InRange(wait!.Value.TotalSeconds, 89, 100);
    }
}
