using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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
/// Sends a real "retry budget exhausted" alert through <see cref="NativeSmtpHandler"/> to a local
/// MailHog instance and reads it back over MailHog's own API — the one part of the feature no unit
/// test can prove, because it depends on an actual SMTP handshake succeeding.
/// </summary>
/// <remarks>
/// Requires MailHog running locally: <c>docker run -d -p 1025:1025 -p 8025:8025 mailhog/mailhog</c>.
/// Skips itself when MailHog is not reachable, so it never fails a normal test run.
/// </remarks>
[Collection("Bitween")]
public class RetryAlertServiceTests
{
    private const string MailHogApi = "http://localhost:8025/api/v2";

    // MailHog is local and answers instantly or not at all, so the default 100 seconds only ever
    // means "this optional test hangs the run".
    private static readonly TimeSpan MailHogTimeout = TimeSpan.FromSeconds(5);
    private readonly BitweenFixture _fixture;

    public RetryAlertServiceTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<bool> MailHogIsReachable()
    {
        try
        {
            using var http = new HttpClient { Timeout = MailHogTimeout };
            var response = await http.GetAsync($"{MailHogApi}/messages");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // Deleting is only exposed on MailHog's v1 API — the v2 route 404s and would silently leave
    // messages behind, making the assertions depend on leftovers from the previous run.
    private static async Task ClearMailHog()
    {
        using var http = new HttpClient { Timeout = MailHogTimeout };
        var response = await http.DeleteAsync("http://localhost:8025/api/v1/messages");
        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement?> LatestMailHogMessage()
    {
        using var http = new HttpClient { Timeout = MailHogTimeout };
        var json = await http.GetStringAsync($"{MailHogApi}/messages");
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items").Clone();
        return items.GetArrayLength() > 0 ? items[0] : null;
    }

    private static async Task<int> MailHogTotal()
    {
        using var http = new HttpClient { Timeout = MailHogTimeout };
        var json = await http.GetStringAsync($"{MailHogApi}/messages");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("total").GetInt32();
    }

    [Fact]
    public async Task Exhausted_budget_alert_arrives_in_MailHog_with_the_group_and_subscription_named()
    {
        if (!await MailHogIsReachable())
            return; // Environment doesn't have MailHog running — nothing to verify against.

        await ClearMailHog();

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var alertService = scope.ServiceProvider.GetRequiredService<RetryAlertService>();

        var doc = new Document(7201, "MailHog Alert Doc");
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        // A group whose own alert config points at MailHog directly — the narrowest level, so the
        // resolver has nothing to fall through to and the test proves that level specifically.
        var groupId = Guid.NewGuid();
        var policy = new RetryPolicy
        {
            Name = "MailHog Alert Policy",
            Groups =
            [
                new RetryGroup
                {
                    Id = groupId,
                    Name = "FRT charges cannot be found",
                    Priority = 10,
                    AppliesTo = [XchangeResultType.Error],
                    Matchers = [new ContainsMatcher { Value = "timeout" }],
                    Budget = new RetryBudget
                    {
                        MaxAttemptsPerError = 1,
                        MaxAttemptsTotal = 1,
                        DelayStrategy = new FixedDelayStrategy { DelayMs = 1000 }
                    },
                    AlertMode = RetryAlertMode.Send,
                    AlertHandlerId = "NativeSmtpHandler",
                    AlertHandlerProperties = new Dictionary<string, string>
                    {
                        ["Host"] = "localhost",
                        ["Port"] = "1025",
                        ["UseTls"] = "false",
                        ["From"] = "bitween-alerts@example.com",
                        ["To"] = "ops@example.com",
                        ["Subject"] = "Retries stopped for {{ SubscriptionName }}",
                        ["Body"] = "{{ GroupName }} used all {{ MaxAttemptsTotal }} retries."
                    }
                }
            ]
        };
        db.Set<RetryPolicy>().Add(policy);
        await db.SaveChangesAsync();

        var sub = new Subscription("MailHog Alert Sub", doc.Id);
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();
        sub.SetRetryPolicy(policy.Id, null);
        await db.SaveChangesAsync();

        var xchange = await scope.ServiceProvider.GetRequiredService<XchangeService>()
            .CreateXchange(sub, new XchangeFile("{}"));
        await db.SaveChangesAsync();

        // Reproduces exactly what TryScheduleAutoRetry does: evaluate against the real budget table,
        // and when it reports exhaustion, raise the event the same way XchangeResult does in
        // production. RetryAlertService.Process is then invoked directly rather than over the bus —
        // this suite calls handlers directly throughout (see RetryJobTests, DelayedRetriesTests)
        // rather than relying on live message transport, which is SW.Bus's own concern, not this
        // feature's.
        var evaluator = new RetryPolicyEvaluator(policy,
            new RetryGroupBudget(db, scope.ServiceProvider, sub.Id));

        // Total is 1, so the first message (a different "parcel" failing the same way) spends the
        // whole budget and is itself allowed to retry — exhaustion only shows up for the next one.
        var firstMessage = await evaluator.Evaluate(XchangeResultType.Error,
            "System.TimeoutException: contains timeout", 0);
        Assert.True(firstMessage.ShouldRetry);

        var decision = await evaluator.Evaluate(XchangeResultType.Error,
            "System.TimeoutException: contains timeout", 0);
        Assert.False(decision.ShouldRetry);
        Assert.True(decision.BudgetJustExhausted);

        var xchangeResult = new XchangeResult(xchange.Id, null, null, exception: "System.TimeoutException: contains timeout");
        xchangeResult.RaiseBudgetExhausted(sub.Id, groupId, decision.MatchedGroup!.Name,
            decision.MatchedGroup.Budget!.MaxAttemptsTotal);

        // SaveChangesAsync dispatches and clears Events (see BitweenDbContext), same as it does in
        // production, so the event has to be captured before saving rather than read back after.
        // The constructor raises its own XchangeResultCreatedEvent alongside it.
        var raisedEvent = Assert.Single(xchangeResult.Events.OfType<RetryBudgetExhaustedEvent>());

        db.Add(xchangeResult);
        await db.SaveChangesAsync();

        await alertService.Process(raisedEvent);

        var message = await LatestMailHogMessage();
        Assert.NotNull(message);

        var subject = message!.Value.GetProperty("Content").GetProperty("Headers")
            .GetProperty("Subject")[0].GetString();
        Assert.Equal("Retries stopped for MailHog Alert Sub", subject);

        var body = message.Value.GetProperty("Content").GetProperty("Body").GetString();
        Assert.Contains("FRT charges cannot be found used all 1 retries", body);

        var loggedNotification = await db.Set<XchangeNotification>().AsNoTracking()
            .SingleAsync(n => n.XchangeId == xchange.Id);
        Assert.True(loggedNotification.Success);
        Assert.Equal(XchangeNotification.RetryBudgetAlertName, loggedNotification.NotifierName);

        // Redelivery of the same event must not double-send — same guard the real bus retry path
        // relies on. Compared against the count after the first send rather than an absolute
        // number, so a stray message could never make this pass by accident.
        var totalAfterFirstSend = await MailHogTotal();
        await alertService.Process(raisedEvent);
        Assert.Equal(totalAfterFirstSend, await MailHogTotal());
    }

    [Fact]
    public async Task A_failed_send_does_not_stop_a_later_delivery()
    {
        if (!await MailHogIsReachable())
            return; // Environment doesn't have MailHog running — nothing to verify against.

        await ClearMailHog();

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var alertService = scope.ServiceProvider.GetRequiredService<RetryAlertService>();

        var doc = new Document(7202, "Failed Send Doc");
        db.Set<Document>().Add(doc);
        await db.SaveChangesAsync();

        var groupId = Guid.NewGuid();
        var policy = new RetryPolicy
        {
            Name = "Failed Send Policy",
            Groups =
            [
                new RetryGroup
                {
                    Id = groupId,
                    Name = "Timeout",
                    Priority = 10,
                    AppliesTo = [XchangeResultType.Error],
                    Matchers = [new ContainsMatcher { Value = "timeout" }],
                    Budget = new RetryBudget
                    {
                        MaxAttemptsPerError = 1,
                        MaxAttemptsTotal = 1,
                        DelayStrategy = new FixedDelayStrategy { DelayMs = 1000 }
                    },
                    AlertMode = RetryAlertMode.Send,
                    AlertHandlerId = "NativeSmtpHandler",
                    AlertHandlerProperties = new Dictionary<string, string>
                    {
                        ["Host"] = "localhost",
                        ["Port"] = "1025",
                        ["UseTls"] = "false",
                        ["From"] = "bitween-alerts@example.com",
                        ["To"] = "ops@example.com",
                        ["Subject"] = "Retries stopped for {{ SubscriptionName }}",
                        ["Body"] = "{{ GroupName }} used all {{ MaxAttemptsTotal }} retries."
                    }
                }
            ]
        };
        db.Set<RetryPolicy>().Add(policy);
        await db.SaveChangesAsync();

        var sub = new Subscription("Failed Send Sub", doc.Id);
        db.Set<Subscription>().Add(sub);
        await db.SaveChangesAsync();
        sub.SetRetryPolicy(policy.Id, null);
        await db.SaveChangesAsync();

        var xchange = await scope.ServiceProvider.GetRequiredService<XchangeService>()
            .CreateXchange(sub, new XchangeFile("{}"));
        await db.SaveChangesAsync();

        // Stands in for a first attempt that threw — a dropped connection, a refused relay. Written
        // directly because what matters is the row it leaves behind, not how the send failed.
        db.Add(XchangeNotification.ForRetryBudgetAlert(xchange.Id, "System.Net.Sockets.SocketException: refused"));
        await db.SaveChangesAsync();

        var xchangeResult = new XchangeResult(xchange.Id, null, null, exception: "timeout");
        xchangeResult.RaiseBudgetExhausted(sub.Id, groupId, "Timeout", 1);
        var raisedEvent = Assert.Single(xchangeResult.Events.OfType<RetryBudgetExhaustedEvent>());
        db.Add(xchangeResult);
        await db.SaveChangesAsync();

        // The recoverable failure must not read as "already delivered": a transient error would
        // otherwise silence the alert for good, which is the opposite of what a retry system owes.
        await alertService.Process(raisedEvent);

        Assert.Equal(1, await MailHogTotal());
        Assert.True(await db.Set<XchangeNotification>()
            .AnyAsync(n => n.XchangeId == xchange.Id
                           && n.NotifierName == XchangeNotification.RetryBudgetAlertName
                           && n.Success));

        // And now that one did get through, the guard has to hold: no third row, no second email.
        await alertService.Process(raisedEvent);
        Assert.Equal(1, await MailHogTotal());
    }
}
