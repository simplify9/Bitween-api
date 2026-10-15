using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.NativeAdapters;
using SW.Bitween.NativeAdapters.FileTransfers;
using SW.Bitween.NativeAdapters.SmtpHandler;
using SW.PrimitiveTypes;

namespace SW.Bitween.UnitTests;

[TestClass]
public class DestinationCircuitsTests
{
    const string Erp = "https://erp.example";

    sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(int seconds) => Now = Now.AddSeconds(seconds);
    }

    static (DestinationCircuits Circuits, Clock Clock) Breaker(int failures = 3, int window = 60, int open = 60)
    {
        var clock = new Clock();
        return (new DestinationCircuits(new CircuitBreakerSettings(failures, window, open), clock), clock);
    }

    [TestMethod]
    public void Per_host_by_default_per_endpoint_when_asked_and_a_template_shares_one_circuit()
    {
        var perHost = new DestinationCircuits(new CircuitBreakerSettings());
        var perEndpoint = new DestinationCircuits(new CircuitBreakerSettings(PerEndpoint: true));
        var warehouse = new Uri("http://user:pw@partner:8080/warehouse/orders?x=1");

        Assert.AreEqual("http://partner:8080", perHost.HttpDestination(warehouse, null));
        Assert.AreEqual("http://partner:8080/warehouse/orders", perEndpoint.HttpDestination(warehouse, null));
        Assert.AreNotEqual(perEndpoint.HttpDestination(new Uri("http://partner:8080/marketplace/stock"), null),
            perEndpoint.HttpDestination(warehouse, null));
        Assert.AreEqual("http://partner:8080/orders",
            perEndpoint.HttpDestination(new Uri("http://partner:8080/orders/A-17"), "http://partner:8080/orders/{{ id }}"));
    }

    static void Fail(DestinationCircuits circuits, string destination = Erp)
    {
        using var pass = circuits.Enter(destination);
        pass.Failed();
    }

    static void Succeed(DestinationCircuits circuits, string destination = Erp)
    {
        using var pass = circuits.Enter(destination);
        pass.Succeeded();
    }

    [TestMethod]
    public void Failures_in_a_row_open_the_circuit_and_calls_then_fail_at_once_as_a_503()
    {
        var (circuits, _) = Breaker();
        Fail(circuits);
        Fail(circuits);
        Succeed(circuits);
        Fail(circuits);
        Fail(circuits);
        circuits.Enter(Erp).Dispose();
        Fail(circuits);

        var ex = Assert.ThrowsException<CircuitOpenException>(() => circuits.Enter(Erp));

        Assert.AreEqual("503: ServiceUnavailable: circuit open for https://erp.example (3 failures in a row; trying again after 10:01:00Z)", ex.Message);
        Assert.AreEqual(Erp, ex.Destination);
    }

    [TestMethod]
    public void A_success_between_failures_starts_the_count_again()
    {
        var (circuits, _) = Breaker();
        for (var i = 0; i < 10; i++)
        {
            Fail(circuits);
            Fail(circuits);
            Succeed(circuits);
        }
        circuits.Enter(Erp).Dispose();
    }

    [TestMethod]
    public void Failures_spread_wider_than_the_window_do_not_open_it()
    {
        var (circuits, clock) = Breaker(window: 60);
        for (var i = 0; i < 10; i++)
        {
            Fail(circuits);
            clock.Advance(31);
        }
        circuits.Enter(Erp).Dispose();

        Fail(circuits);
        clock.Advance(1);
        Fail(circuits);
        Assert.ThrowsException<CircuitOpenException>(() => circuits.Enter(Erp));
    }

    [TestMethod]
    public void Each_destination_has_its_own_circuit()
    {
        var (circuits, _) = Breaker();
        for (var i = 0; i < 3; i++) Fail(circuits);

        Assert.ThrowsException<CircuitOpenException>(() => circuits.Enter(Erp));
        circuits.Enter("https://warehouse.example").Dispose();
        circuits.Enter("https://erp.example:8443").Dispose();
    }

    [TestMethod]
    public void After_the_cool_down_one_trial_goes_through_and_its_success_closes_the_circuit()
    {
        var (circuits, clock) = Breaker(open: 60);
        for (var i = 0; i < 3; i++) Fail(circuits);

        clock.Advance(59);
        Assert.ThrowsException<CircuitOpenException>(() => circuits.Enter(Erp));
        clock.Advance(1);

        var trial = circuits.Enter(Erp);
        var ex = Assert.ThrowsException<CircuitOpenException>(() => circuits.Enter(Erp), "one trial at a time");
        StringAssert.Contains(ex.Message, "a trial call is under way");

        trial.Succeeded();
        circuits.Enter(Erp).Dispose();
        Succeed(circuits);
        // Closed means counting from nothing again.
        Fail(circuits);
        Fail(circuits);
        circuits.Enter(Erp).Dispose();
    }

    [TestMethod]
    public void A_failed_trial_opens_the_circuit_for_another_cool_down()
    {
        var (circuits, clock) = Breaker(open: 60);
        for (var i = 0; i < 3; i++) Fail(circuits);
        clock.Advance(60);

        Fail(circuits);

        var ex = Assert.ThrowsException<CircuitOpenException>(() => circuits.Enter(Erp));
        StringAssert.Contains(ex.Message, "the trial call failed; trying again after 10:02:00Z");
        clock.Advance(60);
        Succeed(circuits);
        circuits.Enter(Erp).Dispose();
    }

    [TestMethod]
    public void A_trial_that_says_nothing_about_the_destination_lets_the_next_call_try()
    {
        var (circuits, clock) = Breaker();
        for (var i = 0; i < 3; i++) Fail(circuits);
        clock.Advance(60);

        using (var trial = circuits.Enter(Erp))
            trial.Failed(new SWException("The login response has no token at 'jwt'."));

        var next = circuits.Enter(Erp);
        Assert.ThrowsException<CircuitOpenException>(() => circuits.Enter(Erp));
        next.Succeeded();
    }

    [TestMethod]
    public void A_trial_that_never_reports_back_does_not_keep_the_circuit_shut()
    {
        var (circuits, clock) = Breaker(open: 60);
        for (var i = 0; i < 3; i++) Fail(circuits);
        clock.Advance(60);
        _ = circuits.Enter(Erp);

        clock.Advance(59);
        Assert.ThrowsException<CircuitOpenException>(() => circuits.Enter(Erp));
        clock.Advance(1);
        circuits.Enter(Erp).Succeeded();
    }

    [TestMethod]
    public void Late_failures_from_calls_let_in_before_it_opened_do_not_extend_it()
    {
        var (circuits, clock) = Breaker(open: 60);
        var late = circuits.Enter(Erp);
        for (var i = 0; i < 3; i++) Fail(circuits);
        clock.Advance(30);
        late.Failed();

        clock.Advance(30);
        circuits.Enter(Erp).Succeeded();
    }

    [TestMethod]
    public void Zero_failures_switches_it_off()
    {
        var (circuits, _) = Breaker(failures: 0);
        for (var i = 0; i < 100; i++) Fail(circuits);
        circuits.Enter(Erp).Dispose();
        Assert.IsFalse(DestinationCircuits.None.Enabled);
    }

    [DataTestMethod]
    [DataRow(HttpStatusCode.TooManyRequests, true)]
    [DataRow(HttpStatusCode.InternalServerError, true)]
    [DataRow(HttpStatusCode.BadGateway, true)]
    [DataRow(HttpStatusCode.ServiceUnavailable, true)]
    [DataRow(HttpStatusCode.GatewayTimeout, true)]
    [DataRow(HttpStatusCode.NotImplemented, false)]
    [DataRow(HttpStatusCode.BadRequest, false)]
    [DataRow(HttpStatusCode.Unauthorized, false)]
    [DataRow(HttpStatusCode.NotFound, false)]
    [DataRow(HttpStatusCode.Conflict, false)]
    [DataRow(HttpStatusCode.UnprocessableEntity, false)]
    [DataRow(HttpStatusCode.RequestTimeout, false)]
    [DataRow(HttpStatusCode.OK, false)]
    public void Which_statuses_count(HttpStatusCode status, bool counts) =>
        Assert.AreEqual(counts, DestinationCircuits.IsUnwell(status));

    [TestMethod]
    public void Which_failures_count()
    {
        Assert.IsTrue(DestinationCircuits.IsUnwell(new TimeoutException()));
        Assert.IsTrue(DestinationCircuits.IsUnwell(new TaskCanceledException()));
        Assert.IsTrue(DestinationCircuits.IsUnwell(new HttpRequestException(HttpRequestError.ConnectionError, "refused",
            new SocketException((int)SocketError.ConnectionRefused))));
        Assert.IsTrue(DestinationCircuits.IsUnwell(new HttpRequestException("reset", new IOException("connection reset"))));
        Assert.IsTrue(DestinationCircuits.IsUnwell(new Renci.SshNet.Common.SshOperationTimeoutException()));
        Assert.IsTrue(DestinationCircuits.IsUnwell(new Renci.SshNet.Common.SshConnectionException()));
        Assert.IsTrue(DestinationCircuits.IsUnwell(new MailKit.Net.Smtp.SmtpCommandException(
            MailKit.Net.Smtp.SmtpErrorCode.UnexpectedStatusCode, MailKit.Net.Smtp.SmtpStatusCode.ServiceNotAvailable, "421 busy")));

        Assert.IsFalse(DestinationCircuits.IsUnwell(new SWException("The login response is not JSON")));
        Assert.IsFalse(DestinationCircuits.IsUnwell(new ArgumentException("Unknown DataEncoding")));
        Assert.IsFalse(DestinationCircuits.IsUnwell(new FileNotFoundException()));
        Assert.IsFalse(DestinationCircuits.IsUnwell(new Renci.SshNet.Common.SshAuthenticationException()));
        Assert.IsFalse(DestinationCircuits.IsUnwell(new MailKit.Net.Smtp.SmtpCommandException(
            MailKit.Net.Smtp.SmtpErrorCode.RecipientNotAccepted, MailKit.Net.Smtp.SmtpStatusCode.MailboxUnavailable, "550 no such user")));
        Assert.IsFalse(DestinationCircuits.IsUnwell(new CircuitOpenException(Erp, "503: open")));
    }

    [TestMethod]
    public async Task The_http_handler_stops_calling_a_failing_destination_and_keeps_calling_a_healthy_one()
    {
        var (circuits, clock) = Breaker();
        var calls = new List<string>();
        var server = new Scripted(request =>
        {
            calls.Add(request.RequestUri!.Host);
            return new HttpResponseMessage(request.RequestUri.Host == "erp.example"
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.OK) { Content = new StringContent("{}") };
        });

        for (var i = 0; i < 3; i++)
            await Assert.ThrowsExceptionAsync<Exception>(() => Http(server, circuits, "https://erp.example/orders").Handle(new XchangeFile("{}")));

        var ex = await Assert.ThrowsExceptionAsync<CircuitOpenException>(() =>
            Http(server, circuits, "https://erp.example/invoices").Handle(new XchangeFile("{}")));
        StringAssert.StartsWith(ex.Message, "503: ServiceUnavailable: circuit open for https://erp.example (");
        Assert.AreEqual(3, calls.Count, "the open circuit sent nothing");

        await Http(server, circuits, "https://warehouse.example/orders").Handle(new XchangeFile("{}"));
        Assert.AreEqual(4, calls.Count);

        clock.Advance(60);
        await Assert.ThrowsExceptionAsync<Exception>(() => Http(server, circuits, "https://erp.example/orders").Handle(new XchangeFile("{}")));
        Assert.AreEqual(5, calls.Count, "the trial went through");
    }

    [TestMethod]
    public async Task Business_rejections_never_open_the_http_handlers_circuit()
    {
        var (circuits, _) = Breaker();
        var server = new Scripted(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity) { Content = new StringContent("no") });

        for (var i = 0; i < 10; i++)
            Assert.IsTrue((await Http(server, circuits, "https://erp.example/orders").Handle(new XchangeFile("{}"))).BadData);
        Assert.AreEqual(10, server.Requests.Count);
    }

    [TestMethod]
    public void The_other_handlers_take_the_registry_or_go_without()
    {
        // Constructed bare, as tests and older callers do, they run without a breaker.
        _ = new NativeSmtpHandler();
        _ = new NativeSftpUploadHandler();
        _ = new NativeFtpUploadHandler(Breaker().Circuits);
    }

    [TestMethod]
    public async Task A_file_upload_to_an_unreachable_server_counts_and_then_fails_at_once()
    {
        var (circuits, _) = Breaker(failures: 2);
        // Bound and never listened on, so connecting is refused straight away.
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)socket.LocalEndPoint!).Port;

        NativeFtpUploadHandler Upload()
        {
            var handler = new NativeFtpUploadHandler(circuits);
            handler.InitializeStartupValues(new Dictionary<string, string>
            {
                ["Host"] = "127.0.0.1", ["Port"] = port.ToString(), ["Username"] = "u", ["Password"] = "p",
            });
            return handler;
        }

        for (var i = 0; i < 2; i++)
        {
            try
            {
                await Upload().Handle(new XchangeFile("x"));
                Assert.Fail("nothing listens there");
            }
            catch (Exception e) when (e is not CircuitOpenException and not AssertFailedException) { }
        }

        var ex = await Assert.ThrowsExceptionAsync<CircuitOpenException>(() => Upload().Handle(new XchangeFile("x")));
        StringAssert.StartsWith(ex.Message, $"503: ServiceUnavailable: circuit open for ftp://127.0.0.1:{port} (");
    }

    static NativeHttpHandler Http(Scripted server, DestinationCircuits circuits, string url)
    {
        var handler = new NativeHttpHandler(new Proxy(new HttpClient(server)), circuits);
        handler.InitializeStartupValues(new Dictionary<string, string> { ["Url"] = url });
        return handler;
    }

    sealed class Proxy(HttpClient client) : IDynamicHttpProxy
    {
        public HttpClient GetClient(string fullUrl) => client;
    }

    sealed class Scripted(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }
}
