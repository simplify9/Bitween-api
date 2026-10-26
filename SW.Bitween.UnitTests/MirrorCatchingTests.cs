using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SW.Bitween.Services.Mirror;

namespace SW.Bitween.UnitTests;

/// <summary>
/// What a mirror does with the calls flows make: a test address, or a host staging's own setup doesn't
/// name, is answered by staging; a host the setup names is called for real. For Bitween's own adapters
/// and for adapters' own processes, which reach staging through the catcher.
/// </summary>
[TestClass]
public class MirrorCatchingTests
{
    const string Test = "m1a2b3c4d.mirror.invalid";

    static readonly MirrorSetupHosts Setup = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "api.staging.acme.test" },
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) { [Test] = ["Push labels"] },
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["login.mirror.invalid"] = "data.session.key" },
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "login.mirror.invalid/v1/session" });

    // The arrivals "Push labels"'s test took: prod's partner answered one with a label.
    static readonly IReadOnlyList<MirrorFamily> Arrivals =
    [
        new MirrorFamily("x1", "direct", "Push labels", null, [
            new MirrorSample("x1", new DateTime(2026, 10, 20), "success", null, [], null, null,
                new MirrorSampleFile("mirror/tests/a/files/x1/response", "label.json", "json", 20, null), Subscription: "Push labels"),
        ]),
    ];

    static MirrorMock Mock(string passHosts = null)
    {
        var options = new BitweenOptions();
        options.Mirror.PassHosts = passHosts;
        return new MirrorMock(null!, options, NullLogger<MirrorMock>.Instance)
        {
            ReadOverride = () => Task.FromResult(Setup),
            ArrivalsOverride = name => Task.FromResult(name == "Push labels" ? Arrivals : []),
            FileOverride = _ => Task.FromResult(Encoding.UTF8.GetBytes("""{"labelId":"QX-0042"}""")),
        };
    }

    [TestMethod]
    public void A_setting_names_its_hosts_and_test_addresses_are_known_as_such()
    {
        CollectionAssert.AreEquivalent(new[] { "api.acme.com", "acme.com" },
            MirrorFaker.HostsIn("Url", "https://user:pw@api.acme.com:8443/v1?cb=x, mail ops@acme.com").ToList());
        CollectionAssert.AreEquivalent(new[] { "sftp.acme.com", "10.0.0.5" }, MirrorFaker.HostsIn("SftpHost", "sftp.acme.com:22, 10.0.0.5").ToList());
        CollectionAssert.AreEquivalent(new[] { "db.acme.com" }, MirrorFaker.HostsIn("Conn", "Host=db.acme.com;Port=5432;Database=x").ToList());
        Assert.AreEqual(0, MirrorFaker.HostsIn("Url", "https://{{globals.api.host}}/v1").Count());
        Assert.AreEqual(0, MirrorFaker.HostsIn("Folder", "/inbound/orders").Count());

        Assert.IsTrue(MirrorFaker.IsMockHost(Test));
        Assert.IsTrue(MirrorFaker.IsMockHost("192.0.2.17"));
        Assert.IsTrue(MirrorFaker.IsMockHost("[2001:db8::a]"));
        Assert.IsFalse(MirrorFaker.IsMockHost("api.acme.com"));
    }

    [TestMethod]
    public async Task Staging_answers_test_addresses_and_hosts_its_setup_doesnt_name()
    {
        var mock = Mock(passHosts: "rates.example.org");

        Assert.IsTrue(await mock.CatchesAsync(Test));
        Assert.IsTrue(await mock.CatchesAsync("api.zohoapis.eu"), "written inside an adapter, named nowhere in the setup");
        Assert.IsFalse(await mock.CatchesAsync("api.staging.acme.test"), "the team's own staging API, named in the setup");
        Assert.IsFalse(await mock.CatchesAsync("rates.example.org"), "let through on purpose");
        Assert.IsFalse(await mock.CatchesAsync("localhost"));
        Assert.IsFalse(await mock.CatchesAsync("127.0.0.1"));
    }

    [TestMethod]
    public async Task A_login_gets_a_token_where_its_settings_read_it_and_a_call_gets_prods_answer()
    {
        var mock = Mock();

        var login = await mock.AnswerAsync(new MirrorRequest("POST", new Uri("https://login.mirror.invalid/v1/session"), "application/json",
            Encoding.UTF8.GetBytes("""{"user":"x","pass":"y"}"""), "Bitween"));
        var reply = JsonNode.Parse(login.Body)!;
        StringAssert.StartsWith((string)reply["data"]!["session"]!["key"]!, "mirror-");
        Assert.AreEqual((string)reply["access_token"], (string)reply["jwt"]);

        var oauth = await mock.AnswerAsync(new MirrorRequest("POST", new Uri($"https://{Test}/oauth2/token"), "application/x-www-form-urlencoded",
            Encoding.UTF8.GetBytes("grant_type=client_credentials"), "Bitween"));
        Assert.IsNotNull(JsonNode.Parse(oauth.Body)!["access_token"]);

        var label = await mock.AnswerAsync(new MirrorRequest("POST", new Uri($"https://{Test}/v2/labels"), "application/json", [], "Bitween"));
        Assert.AreEqual(200, label.Status);
        Assert.AreEqual("""{"labelId":"QX-0042"}""", Encoding.UTF8.GetString(label.Body));
        StringAssert.Contains(label.Why, "Push labels");

        var unknown = await mock.AnswerAsync(new MirrorRequest("GET", new Uri("https://api.zohoapis.eu/crm/v2"), null, [], "An adapter's process"));
        Assert.AreEqual(200, unknown.Status);
        Assert.AreEqual(0, unknown.Body.Length);
    }

    [TestMethod]
    public async Task Bitweens_own_adapters_are_answered_without_the_network()
    {
        var handler = new MirrorCatchingHandler(Mock()) { InnerHandler = new Unreachable() };
        using var client = new HttpClient(handler);

        var answer = await client.PostAsync($"https://{Test}/v2/labels", new StringContent("{}"));
        Assert.AreEqual(HttpStatusCode.OK, answer.StatusCode);
        Assert.AreEqual("application/json", answer.Content.Headers.ContentType!.MediaType);
        Assert.AreEqual("""{"labelId":"QX-0042"}""", await answer.Content.ReadAsStringAsync());

        // A host the setup names goes on to the network.
        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => client.GetAsync("https://api.staging.acme.test/ping"));
    }

    sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException($"sent on to {request.RequestUri!.Host}");
    }

    [TestMethod]
    public async Task An_adapters_process_is_answered_over_tls_by_the_catcher_and_the_rest_is_passed_on()
    {
        var mock = Mock();
        using var catcher = MirrorCatcher.Listen(new WebProxy());
        using var stop = new CancellationTokenSource();
        var running = catcher.RunAsync(mock, NullLogger.Instance, stop.Token);

        using var client = new HttpClient(new SocketsHttpHandler
        {
            Proxy = new WebProxy($"http://127.0.0.1:{catcher.Port}") { BypassProxyOnLocal = false },
            UseProxy = true,
            // As an adapter's process does, given the bundle the catcher writes: its authority trusted.
            SslOptions = new SslClientAuthenticationOptions { RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                Trusted(catcher.Authority.Certificate, (X509Certificate2)certificate!, errors) },
        });

        var tls = await client.PostAsync($"https://{Test}/v2/labels", new StringContent("""{"order":"A1"}"""));
        Assert.AreEqual("""{"labelId":"QX-0042"}""", await tls.Content.ReadAsStringAsync());
        var again = await client.PostAsync($"https://{Test}/oauth/token", new FormUrlEncodedContent([new("grant_type", "client_credentials")]));
        Assert.IsNotNull(JsonNode.Parse(await again.Content.ReadAsStringAsync())!["access_token"]);
        var plain = await client.GetAsync($"http://{Test}/status");
        Assert.AreEqual(HttpStatusCode.OK, plain.StatusCode);

        // Loopback isn't caught: what the server there says comes back through the catcher.
        var (server, port) = Serve("HTTP/1.1 200 OK\r\nContent-Length: 5\r\nConnection: close\r\n\r\nhello");
        Assert.AreEqual("hello", await client.GetStringAsync($"http://127.0.0.1:{port}/ping"));
        await server;

        // A client that doesn't trust the authority fails, and a test running it hears why.
        mock.Replay("rehearsal-refused", []);
        using var stranger = new HttpClient(new SocketsHttpHandler { Proxy = new WebProxy($"http://127.0.0.1:{catcher.Port}"), UseProxy = true });
        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => stranger.GetAsync($"https://{Test}/v2/labels"));
        for (var i = 0; i < 50 && !mock.CallsSoFar.Any(c => c.Status == 0); i++) await Task.Delay(20);
        StringAssert.Contains(mock.EndReplay().First(c => c.Status == 0).Why, "failed before staging could answer");

        await stop.CancelAsync();
        await running;
    }

    [TestMethod]
    public async Task A_tunnel_to_a_host_staging_doesnt_catch_carries_what_is_sent_through_it()
    {
        using var catcher = MirrorCatcher.Listen(new WebProxy());
        using var stop = new CancellationTokenSource();
        var running = catcher.RunAsync(Mock(), NullLogger.Instance, stop.Token);
        var (server, port) = Serve("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, catcher.Port);
        var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"CONNECT 127.0.0.1:{port} HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\n\r\n"));
        StringAssert.StartsWith(await ReadUntilAsync(stream, "\r\n\r\n"), "HTTP/1.1 200");
        await stream.WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: x\r\n\r\n"));
        StringAssert.EndsWith(await ReadUntilAsync(stream, "ok"), "ok");
        await server;

        await stop.CancelAsync();
        await running;
    }

    static bool Trusted(X509Certificate2 authority, X509Certificate2 certificate, SslPolicyErrors errors)
    {
        if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0) return false;
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(authority);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(certificate);
    }

    /// <summary>A server on loopback that answers one connection with <paramref name="answer"/>.</summary>
    static (Task Served, int Port) Serve(string answer)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return (Task.Run(async () =>
        {
            using var _ = listener.Server;
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            await ReadUntilAsync(stream, "\r\n\r\n");
            await stream.WriteAsync(Encoding.ASCII.GetBytes(answer));
            listener.Stop();
        }), port);
    }

    static async Task<string> ReadUntilAsync(NetworkStream stream, string end)
    {
        var read = new StringBuilder();
        var buffer = new byte[1];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!read.ToString().EndsWith(end, StringComparison.Ordinal) && await stream.ReadAsync(buffer, timeout.Token) > 0)
            read.Append((char)buffer[0]);
        return read.ToString();
    }
}
