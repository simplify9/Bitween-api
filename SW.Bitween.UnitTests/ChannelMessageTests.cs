using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SW.Bitween.Model;
using SW.Bitween.NativeAdapters;
using SW.Bitween.NativeAdapters.ChannelMessages;
using SW.Bitween.NativeAdapters.EmailMessageHandler;
using SW.Bitween.NativeAdapters.SlackMessageHandler;
using SW.Bitween.NativeAdapters.TeamsMessageHandler;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests;

/// <summary>
/// The Email, Teams and Slack channel types: the message Bitween writes for each thing it reports,
/// and how each service is handed it.
/// </summary>
[TestClass]
public class ChannelMessageTests
{
    private static readonly DateTime Noon = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    private static XchangeResultNotification Result(bool success = false, bool responseBad = false) => new()
    {
        Id = "x-1",
        Success = success,
        ResponseBad = responseBad,
        Exception = success ? null : "Timed out after 30s",
        SubscriptionName = "Orders to SAP",
        DocumentName = "Order",
        CorrelationId = "c-1",
        StartedOn = Noon,
        FinishedOn = Noon.AddSeconds(31)
    };

    /// <summary>
    /// A server that never greets, as one expecting TLS from the first byte does when Bitween starts
    /// plainly for STARTTLS, fails at the connect limit, saying what to check, rather than after MailKit's two minutes.
    /// </summary>
    [TestMethod]
    public async Task A_mail_server_that_never_answers_fails_at_the_connect_limit_saying_what_to_check()
    {
        var silent = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        var port = ((IPEndPoint)silent.LocalEndpoint).Port;
        var accepted = silent.AcceptTcpClientAsync();
        try
        {
            var message = new MimeKit.MimeMessage();
            message.From.Add(MimeKit.MailboxAddress.Parse("bitween@acme.example"));
            message.To.Add(MimeKit.MailboxAddress.Parse("ops@acme.example"));
            message.Subject = "Test";
            message.Body = new MimeKit.TextPart("plain") { Text = "Test" };

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var error = await Assert.ThrowsExceptionAsync<TimeoutException>(() => SW.Bitween.NativeAdapters.SmtpHandler.NativeSmtpHandler.Deliver(
                new DestinationCircuits(new CircuitBreakerSettings()), message, "127.0.0.1", port, useTls: true,
                username: null, password: null, from: "bitween@acme.example", certificateThumbprint: null,
                connectLimit: TimeSpan.FromSeconds(1)));

            Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
            StringAssert.Contains(error.Message, $"127.0.0.1:{port} didn't answer within 1 seconds");
            StringAssert.Contains(error.Message, "only on port 465");
        }
        finally
        {
            silent.Stop();
            (await accepted.ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null))?.Dispose();
        }
    }

    [TestMethod]
    public void A_failed_exchange_reads_as_a_failure_with_its_error_and_a_link_to_it()
    {
        var message = ChannelMessages.For(Result(), "https://bitween.example");

        Assert.AreEqual("Orders to SAP failed", message.Title);
        Assert.AreEqual(ChannelMessageTone.Failure, message.Tone);
        Assert.AreEqual("Timed out after 30s", message.Detail);
        Assert.AreEqual("https://bitween.example/exchanges?ids=x-1", message.LinkUrl);
        CollectionAssert.AreEqual(
            new[] { "Subscription", "Information type", "Exchange", "Correlation id", "Started", "Finished" },
            message.Facts.Select(f => f.Label).ToArray());
        Assert.AreEqual("8 Oct 2026, 12:00:00 UTC", message.Facts.Single(f => f.Label == "Started").Value);
    }

    [TestMethod]
    public void A_bad_result_and_a_success_each_read_as_what_they_are()
    {
        var bad = ChannelMessages.For(Result(success: true, responseBad: true), null);
        Assert.AreEqual("Orders to SAP finished with a bad result", bad.Title);
        Assert.AreEqual(ChannelMessageTone.Warning, bad.Tone);

        var good = ChannelMessages.For(Result(success: true), null);
        Assert.AreEqual("Orders to SAP succeeded", good.Title);
        Assert.AreEqual(ChannelMessageTone.Success, good.Tone);
        Assert.IsNull(good.Detail);
    }

    [TestMethod]
    public void A_message_after_a_quiet_window_says_how_many_were_held_back()
    {
        var result = Result();
        Assert.IsNull(ChannelMessages.For(result, null).Text);

        result.SuppressedSinceLast = 3;
        Assert.AreEqual("3 more like this since the last message were not sent separately.",
            ChannelMessages.For(result, null).Text);
    }

    [TestMethod]
    public void Without_an_address_for_Bitween_the_message_goes_without_a_link()
    {
        Assert.IsNull(ChannelMessages.For(Result(), null).LinkUrl);
    }

    [TestMethod]
    public void A_spent_retry_budget_says_which_group_stopped_and_why()
    {
        var message = ChannelMessages.For(new RetryBudgetExhaustedNotification
        {
            XchangeId = "x-9",
            SubscriptionName = "Orders to SAP",
            GroupName = "Timeouts",
            MaxAttemptsTotal = 8,
            PolicyName = null,
            Exception = "Timed out",
            OccurredOn = Noon
        }, "https://bitween.example");

        Assert.AreEqual("Retries stopped for Orders to SAP", message.Title);
        Assert.AreEqual(ChannelMessageTone.Failure, message.Tone);
        StringAssert.Contains(message.Text, "\"Timeouts\" used its whole budget of 8 retries");
        Assert.AreEqual("Its own policy", message.Facts.Single(f => f.Label == "Retry policy").Value);
        Assert.AreEqual("https://bitween.example/exchanges?ids=x-9", message.LinkUrl);
    }

    [TestMethod]
    public void A_subscription_that_paused_itself_says_how_many_failures_and_links_to_it()
    {
        var message = ChannelMessages.For(new SubscriptionAutoPausedNotification
        {
            SubscriptionId = 7,
            SubscriptionName = "Orders to SAP",
            Failures = 5,
            XchangeId = "x-5",
            Exception = "Timed out",
            OccurredOn = Noon
        }, "https://bitween.example");

        Assert.AreEqual("Orders to SAP paused itself", message.Title);
        Assert.AreEqual(ChannelMessageTone.Failure, message.Tone);
        StringAssert.Contains(message.Text, "failed 5 times in a row");
        Assert.AreEqual("https://bitween.example/subscriptions/7", message.LinkUrl);
    }

    [TestMethod]
    public void Going_quiet_and_coming_back_read_as_what_they_are()
    {
        XchangeResultNotification Quiet(string kind) => new()
        {
            Id = $"{kind}-7", SubscriptionId = 7, SubscriptionName = "Orders to SAP", DocumentName = "Order",
            Event = kind, Success = kind == QuietSubscriptionsJob.Back,
            Exception = "No exchanges since 2026-10-08 11:00 UTC", FinishedOn = Noon
        };

        var quiet = ChannelMessages.For(Quiet(QuietSubscriptionsJob.GoneQuiet), "https://bitween.example");
        Assert.AreEqual("Orders to SAP went quiet", quiet.Title);
        Assert.AreEqual(ChannelMessageTone.Warning, quiet.Tone);
        Assert.AreEqual("No exchanges since 2026-10-08 11:00 UTC", quiet.Text);
        Assert.AreEqual("https://bitween.example/subscriptions/7", quiet.LinkUrl);

        var back = ChannelMessages.For(Quiet(QuietSubscriptionsJob.Back), null);
        Assert.AreEqual("Orders to SAP is receiving again", back.Title);
        Assert.AreEqual(ChannelMessageTone.Success, back.Tone);
    }

    [TestMethod]
    public void Anything_but_a_message_is_refused_by_the_message_adapters()
    {
        Assert.ThrowsException<InvalidOperationException>(() => ChannelMessage.Parse("{\"Id\":\"x-1\"}"));
        Assert.ThrowsException<InvalidOperationException>(() => ChannelMessage.Parse("not json"));
        Assert.AreEqual("Hi", ChannelMessage.Parse(JsonConvert.SerializeObject(new ChannelMessage { Title = "Hi" })).Title);
    }

    [TestMethod]
    public void Teams_gets_one_adaptive_card_wrapped_in_a_message()
    {
        var json = JObject.Parse(TeamsCard.Render(ChannelMessages.For(Result(), "https://bitween.example")));

        Assert.AreEqual("message", (string)json["type"]);
        var attachment = json["attachments"]![0]!;
        Assert.AreEqual("application/vnd.microsoft.card.adaptive", (string)attachment["contentType"]);

        var card = attachment["content"]!;
        Assert.AreEqual("AdaptiveCard", (string)card["type"]);
        Assert.AreEqual("1.4", (string)card["version"]);
        Assert.AreEqual("Orders to SAP failed", (string)card["body"]![0]!["text"]);
        Assert.AreEqual("Attention", (string)card["body"]![0]!["color"]);
        Assert.AreEqual("FactSet", (string)card["body"]![1]!["type"]);
        Assert.AreEqual("https://bitween.example/exchanges?ids=x-1", (string)card["actions"]![0]!["url"]);
    }

    [TestMethod]
    public void Slack_gets_blocks_cut_to_its_limits_with_its_markup_escaped()
    {
        var message = new ChannelMessage
        {
            Title = "Orders to SAP failed",
            Tone = ChannelMessageTone.Failure,
            // Slack rejects a section over 3,000 characters, and twelve fields would be two over its ten.
            Facts = Enumerable.Range(1, 12).Select(i => new ChannelMessageFact($"Fact {i}", $"<value {i}>")).ToList(),
            Detail = "<html> & " + new string('x', 5000)
        };

        var json = JObject.Parse(SlackBlocks.Render(message));
        var blocks = json["blocks"]!.ToArray();

        Assert.AreEqual("Orders to SAP failed", (string)json["text"]);
        Assert.AreEqual(":x: Orders to SAP failed", (string)blocks[0]["text"]!["text"]);

        var fieldSections = blocks.Where(b => b["fields"] != null).ToArray();
        Assert.AreEqual(2, fieldSections.Length);
        Assert.AreEqual(10, fieldSections[0]["fields"]!.Count());
        Assert.AreEqual("*Fact 1*\n&lt;value 1&gt;", (string)fieldSections[0]["fields"]![0]!["text"]);

        var detail = (string)blocks.Last(b => b["type"]!.ToString() == "section")["text"]!["text"];
        Assert.IsTrue(detail.Length <= 3000, $"The detail section is {detail.Length} characters.");
        StringAssert.StartsWith(detail, "```&lt;html&gt; &amp; ");
    }

    [TestMethod]
    public void An_email_shows_markup_from_an_error_as_text()
    {
        var (subject, html, text) = EmailBody.Render(new ChannelMessage
        {
            Title = "Orders\nfailed",
            Detail = "<script>alert(1)</script>",
            LinkUrl = "https://bitween.example/exchanges?ids=a&b",
            LinkLabel = "Open the exchange"
        });

        Assert.AreEqual("Orders failed", subject);
        StringAssert.Contains(html, "&lt;script&gt;alert(1)&lt;/script&gt;");
        Assert.IsFalse(html.Contains("<script>"));
        StringAssert.Contains(html, "href=\"https://bitween.example/exchanges?ids=a&amp;b\"");
        StringAssert.Contains(text, "Open the exchange: https://bitween.example/exchanges?ids=a&b");
    }

    [TestMethod]
    public async Task The_Slack_adapter_posts_to_its_webhook_and_reports_what_Slack_said_when_refused()
    {
        var factory = new RecordingHttpClientFactory();
        var handler = new NativeSlackMessageHandler(factory);
        handler.InitializeStartupValues(new Dictionary<string, string> { ["WebhookUrl"] = "https://hooks.slack.test/T/B/x" });
        var payload = JsonConvert.SerializeObject(ChannelMessages.For(Result(), null));

        await handler.Handle(new XchangeFile(payload, "x-1"));

        var request = factory.Requests.Single();
        Assert.AreEqual(HttpMethod.Post, request.Method);
        Assert.AreEqual("https://hooks.slack.test/T/B/x", request.Uri);
        Assert.AreEqual("Orders to SAP failed", (string)JObject.Parse(request.Body)["text"]);

        factory.Reply = () => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("no_service") };
        var refused = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            handler.Handle(new XchangeFile(payload, "x-1")));
        StringAssert.Contains(refused.Message, "Slack refused the message (404");
        StringAssert.Contains(refused.Message, "no_service");
    }

    [TestMethod]
    public async Task The_Teams_adapter_will_not_post_anywhere_but_https()
    {
        var factory = new RecordingHttpClientFactory();
        var handler = new NativeTeamsMessageHandler(factory);
        handler.InitializeStartupValues(new Dictionary<string, string> { ["WebhookUrl"] = "http://teams.test/hook" });

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            handler.Handle(new XchangeFile(JsonConvert.SerializeObject(new ChannelMessage { Title = "Hi" }), "x")));
        Assert.AreEqual(0, factory.Requests.Count);
    }

    [TestMethod]
    public async Task A_webhook_that_keeps_failing_opens_its_circuit_and_is_not_called_again()
    {
        var factory = new RecordingHttpClientFactory
        {
            Reply = () => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("down") }
        };
        var handler = new NativeSlackMessageHandler(factory, new DestinationCircuits(new CircuitBreakerSettings(Failures: 2)));
        handler.InitializeStartupValues(new Dictionary<string, string> { ["WebhookUrl"] = "https://hooks.slack.test/T/B/x" });
        var payload = new XchangeFile(JsonConvert.SerializeObject(new ChannelMessage { Title = "Hi" }), "x");

        for (var i = 0; i < 2; i++)
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => handler.Handle(payload));

        await Assert.ThrowsExceptionAsync<CircuitOpenException>(() => handler.Handle(payload));
        Assert.AreEqual(2, factory.Requests.Count);
    }

    [TestMethod]
    public void The_channel_types_are_kept_out_of_the_adapter_pickers()
    {
        var factory = new RecordingHttpClientFactory();
        INativeInfolinkHandler[] handlers =
        [
            new NativeEmailMessageHandler(new MailServerOptions()),
            new NativeTeamsMessageHandler(factory),
            new NativeSlackMessageHandler(factory),
            new NativeAdapters.SmtpHandler.NativeSmtpHandler()
        ];
        var discovery = new NativeAdapterDiscoveryService(handlers, [], [], [], handlers, new BitweenOptions());

        CollectionAssert.AreEqual(new[] { "NativeSmtpHandler" }, discovery.GetNativeAdapters("handlers"));
        // Still described, which is what masks a webhook address and checks a channel's settings.
        Assert.IsTrue(discovery.GetStartupValues("NativeSlackMessageHandler")["WebhookUrl"].Private);
    }

    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        public List<(HttpMethod Method, string Uri, string Body)> Requests { get; } = [];
        /// <summary>A new response per call: the adapter disposes each one it is handed.</summary>
        public Func<HttpResponseMessage> Reply { get; set; } =
            () => new(HttpStatusCode.OK) { Content = new StringContent("ok") };

        public HttpClient CreateClient(string name) => new(new Recorder(this));

        private sealed class Recorder(RecordingHttpClientFactory owner) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                owner.Requests.Add((request.Method, request.RequestUri!.ToString(),
                    await request.Content!.ReadAsStringAsync(cancellationToken)));
                return owner.Reply();
            }
        }
    }
}
