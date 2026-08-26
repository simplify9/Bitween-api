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
/// MailHog is started by <see cref="BitweenFixture"/> alongside PostgreSQL and RabbitMQ, so these
/// tests run everywhere the rest of the suite does. They used to return early when a local MailHog
/// was missing, which xunit reports as a pass — a green run then said nothing about whether an alert
/// can actually be delivered.
/// </remarks>
[Collection("Bitween")]
public class RetryAlertServiceTests
{
    // MailHog answers instantly or not at all, so the default 100 seconds only ever means "the run
    // hangs instead of failing".
    private static readonly TimeSpan MailHogTimeout = TimeSpan.FromSeconds(5);

    private readonly BitweenFixture _fixture;

    private string MessagesApi => $"{_fixture.MailHogApi}/api/v2/messages";

    public RetryAlertServiceTests(BitweenFixture fixture)
    {
        _fixture = fixture;
    }

    // Deleting is only exposed on MailHog's v1 API — the v2 route 404s and would silently leave
    // messages behind, making the assertions depend on leftovers from the previous run.
    private async Task ClearMailHog()
    {
        using var http = new HttpClient { Timeout = MailHogTimeout };
        var response = await http.DeleteAsync($"{_fixture.MailHogApi}/api/v1/messages");
        response.EnsureSuccessStatusCode();
    }

    private async Task<JsonElement?> LatestMailHogMessage()
    {
        using var http = new HttpClient { Timeout = MailHogTimeout };
        var json = await http.GetStringAsync(MessagesApi);
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items").Clone();
        return items.GetArrayLength() > 0 ? items[0] : null;
    }

    private async Task<int> MailHogTotal()
    {
        using var http = new HttpClient { Timeout = MailHogTimeout };
        var json = await http.GetStringAsync(MessagesApi);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("total").GetInt32();
    }

    [Fact]
    public async Task Exhausted_budget_alert_arrives_in_MailHog_with_the_group_and_subscription_named()
    {
        await ClearMailHog();

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var alertService = scope.ServiceProvider.GetRequiredService<RetryAlertService>();

        var doc = new Document(null, "MailHog Alert Doc", DocumentFormat.Json);
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
                        ["Port"] = _fixture.MailHogSmtpPort.ToString(),
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
        await ClearMailHog();

        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BitweenDbContext>();
        var alertService = scope.ServiceProvider.GetRequiredService<RetryAlertService>();

        var doc = new Document(null, "Failed Send Doc", DocumentFormat.Json);
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
                        ["Port"] = _fixture.MailHogSmtpPort.ToString(),
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
        // Both are asserted — a redelivery that wrongly logged another success while sending nothing
        // would otherwise pass here.
        var rowsAfterDelivery = await db.Set<XchangeNotification>()
            .CountAsync(n => n.XchangeId == xchange.Id);

        await alertService.Process(raisedEvent);

        Assert.Equal(1, await MailHogTotal());
        Assert.Equal(rowsAfterDelivery,
            await db.Set<XchangeNotification>().CountAsync(n => n.XchangeId == xchange.Id));
    }

    [Fact]
    public async Task The_handler_refuses_to_send_a_password_over_an_unencrypted_connection()
    {
        await ClearMailHog();

        await using var scope = _fixture.CreateScope();
        var discovery = scope.ServiceProvider.GetRequiredService<NativeAdapterDiscoveryService>();

        // MailHog speaks plain SMTP on 1025, which is exactly the shape of the mistake worth
        // catching: a working relay, no encryption, and a password to hand over.
        var handler = discovery.GetNativeHandler("NativeSmtpHandler", new Dictionary<string, string>
        {
            ["Host"] = "localhost",
            ["Port"] = _fixture.MailHogSmtpPort.ToString(),
            ["UseTls"] = "false",
            ["Password"] = "hunter2",
            ["From"] = "bitween-alerts@example.com",
            ["To"] = "ops@example.com",
            ["Subject"] = "Should never be sent",
            ["Body"] = "Should never be sent"
        });

        // Matched on the message, not just the type: the handler also throws
        // InvalidOperationException for a missing recipient, so dropping the To above would otherwise
        // leave this passing without ever reaching the credential guard.
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(new XchangeFile("{}")));
        Assert.Contains("will not send a password over an unencrypted connection", refusal.Message);

        // Refusing has to mean refusing: no message, and therefore no password, left the process.
        Assert.Equal(0, await MailHogTotal());
    }
}
