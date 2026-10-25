using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SW.Bitween.NativeAdapters;
using SW.Bitween.Services.Connections;

namespace SW.Bitween.Services.Mirror;

/// <summary>
/// Answers, in place of the network, what Bitween's own adapters call that the mirror catches (see
/// <see cref="MirrorMock"/>), and sends the rest on.
/// </summary>
public sealed class MirrorCatchingHandler(MirrorMock mock) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is not { IsAbsoluteUri: true } url)
            return await base.SendAsync(request, cancellationToken);
        if (await mock.PassesAsync(url.Host) is { } why)
        {
            mock.Passed(request.Method.Method, url.Host, url.AbsolutePath, why, "Bitween");
            return await base.SendAsync(request, cancellationToken);
        }

        var body = request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var answer = await mock.AnswerAsync(new MirrorRequest(request.Method.Method, url, request.Content?.Headers.ContentType?.ToString(), body, "Bitween"));
        if (answer.Fail) throw new HttpRequestException(answer.Why);
        var response = new HttpResponseMessage((HttpStatusCode)answer.Status) { RequestMessage = request, Content = new ByteArrayContent(answer.Body) };
        if (answer.ContentType != null) response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(answer.ContentType);
        return response;
    }
}

/// <summary>
/// A certificate authority made for one run of this Bitween, and the certificates it gives the hosts
/// the catcher answers for, so an adapter's TLS to a test address succeeds. Adapters' processes are
/// told to trust it; nothing else is. A new run makes a new one, and its processes start again with it.
/// </summary>
public sealed class MirrorAuthority : IDisposable
{
    readonly X509Certificate2 authority;
    readonly ConcurrentDictionary<string, X509Certificate2> issued = new(StringComparer.OrdinalIgnoreCase);

    public MirrorAuthority()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Bitween prod mirror, O=Test addresses only", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        authority = Reloaded(request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5)));
    }

    public string Pem => authority.ExportCertificatePem();

    public X509Certificate2 Certificate => authority;

    /// <summary>A certificate for <paramref name="host"/>, made the first time it is asked for.</summary>
    public X509Certificate2 For(string host) => issued.GetOrAdd(host, Issue);

    X509Certificate2 Issue(string host)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={host}", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        if (IPAddress.TryParse(host, out var ip)) names.AddIpAddress(ip);
        else names.AddDnsName(host);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        // Strict verifiers, Python's among them, want the issuer named this way.
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(authority, true, false));
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        using var signed = request.Create(authority, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365), serial);
        using var withKey = signed.CopyWithPrivateKey(key);
        return Reloaded(withKey);
    }

    // Through PKCS #12, so SslStream on every platform can use the private key.
    static X509Certificate2 Reloaded(X509Certificate2 certificate)
    {
        using (certificate) return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), null);
    }

    public void Dispose()
    {
        authority.Dispose();
        foreach (var c in issued.Values) c.Dispose();
    }
}

/// <summary>
/// Where adapters' own processes send their web calls on a mirror. What the mirror catches (see
/// <see cref="MirrorMock"/>) is answered by staging, over TLS made with <see cref="MirrorAuthority"/>;
/// anything else is passed on untouched, through the proxy Bitween itself would use, if any. It
/// listens on loopback only, so only processes in this container can reach it.
/// <para>
/// The processes find it the way every HTTP stack does, through HTTP_PROXY and HTTPS_PROXY, and trust
/// the authority through SSL_CERT_FILE and its kin. They inherit these from Bitween, which sets them
/// before any adapter starts, having first fixed its own proxy as it was, so Bitween's own calls -
/// to prod, to storage - don't change.
/// </para>
/// </summary>
public sealed class MirrorCatcher : IDisposable
{
    const int MaxHead = 32 * 1024;
    const int MaxBody = 32 * 1024 * 1024;
    static readonly TimeSpan Idle = TimeSpan.FromMinutes(2);

    readonly TcpListener listener;
    readonly MirrorAuthority authority;
    readonly IWebProxy onward;
    MirrorMock mock;
    ILogger logger;

    MirrorCatcher(TcpListener listener, MirrorAuthority authority, IWebProxy onward)
    {
        this.listener = listener;
        this.authority = authority;
        this.onward = onward;
    }

    /// <summary>The port it listens on, on 127.0.0.1.</summary>
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public MirrorAuthority Authority => authority;

    /// <summary>A catcher listening on loopback, answering nothing until <see cref="RunAsync"/>.</summary>
    /// <param name="onward">The proxy calls it passes on go through, if any: Bitween's own, as it was before the catcher.</param>
    public static MirrorCatcher Listen(IWebProxy onward = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new MirrorCatcher(listener, new MirrorAuthority(), onward ?? HttpClient.DefaultProxy);
    }

    /// <summary>
    /// On a staging marked as a mirror: Bitween's own adapters' calls go through
    /// <see cref="MirrorCatchingHandler"/>, and adapters' processes are pointed at a catcher - before
    /// anything starts one, since a process reads where to send its calls when it starts.
    /// </summary>
    public static void Install(IServiceCollection services, BitweenOptions options)
    {
        services.AddTransient<MirrorCatchingHandler>();
        services.AddHttpClient(DynamicHttpProxy.ClientName).AddHttpMessageHandler<MirrorCatchingHandler>();

        var catcher = Listen();
        catcher.TellAdapterProcesses(Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var own) ? [own.Host] : []);
        services.AddSingleton(catcher);
        services.AddHostedService<MirrorCatcherService>();
    }

    /// <summary>
    /// Points the adapter processes Bitween starts from now on at this catcher, and has them trust its
    /// authority, through variables they inherit. Bitween's own proxy is fixed first as it is now, so
    /// its own calls don't go through the catcher. <paramref name="direct"/> are hosts processes reach
    /// without it: Bitween's own public address, where adapters fetch their files.
    /// </summary>
    public void TellAdapterProcesses(IEnumerable<string> direct)
    {
        HttpClient.DefaultProxy = HttpClient.DefaultProxy;

        var folder = Path.Combine(Path.GetTempPath(), $"bitween-mirror-{Environment.ProcessId}");
        Directory.CreateDirectory(folder);
        var authorityFile = Path.Combine(folder, "authority.pem");
        File.WriteAllText(authorityFile, authority.Pem);
        // SSL_CERT_FILE replaces the trusted roots rather than adding to them, so the bundle carries
        // the system's too: calls the catcher passes on still meet their real certificates.
        var bundle = Path.Combine(folder, "trust.pem");
        File.WriteAllText(bundle, SystemRoots() + "\n" + authority.Pem);

        var proxy = $"http://127.0.0.1:{Port}";
        var bypass = string.Join(",", new[] { Environment.GetEnvironmentVariable("NO_PROXY"), "localhost", "127.0.0.1", "::1" }
            .Concat(direct).Where(h => !string.IsNullOrWhiteSpace(h)).Distinct(StringComparer.OrdinalIgnoreCase));
        foreach (var (name, value) in new (string, string)[]
                 {
                     ("HTTP_PROXY", proxy), ("HTTPS_PROXY", proxy), ("http_proxy", proxy), ("https_proxy", proxy),
                     ("NO_PROXY", bypass), ("no_proxy", bypass),
                     // OpenSSL - .NET on Linux, Python, curl - Python's requests, and Node.
                     ("SSL_CERT_FILE", bundle), ("REQUESTS_CA_BUNDLE", bundle), ("CURL_CA_BUNDLE", bundle),
                     ("NODE_EXTRA_CA_CERTS", authorityFile), ("NODE_USE_ENV_PROXY", "1"),
                 })
            Environment.SetEnvironmentVariable(name, value);
    }

    static string SystemRoots()
    {
        foreach (var file in new[]
                 {
                     Environment.GetEnvironmentVariable("SSL_CERT_FILE"), "/etc/ssl/certs/ca-certificates.crt",
                     "/etc/pki/tls/certs/ca-bundle.crt", "/etc/ssl/ca-bundle.pem", "/etc/ssl/cert.pem",
                 })
            if (!string.IsNullOrEmpty(file) && File.Exists(file))
                return File.ReadAllText(file);
        using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        return string.Join("\n", store.Certificates.Select(c => c.ExportCertificatePem()));
    }

    /// <summary>Answers connections until <paramref name="stopping"/>, catching what <paramref name="mirror"/> catches.</summary>
    public async Task RunAsync(MirrorMock mirror, ILogger log, CancellationToken stopping)
    {
        mock = mirror;
        logger = log;
        logger.LogInformation("Prod mirror: adapters' processes send their web calls through 127.0.0.1:{Port}", Port);
        while (!stopping.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stopping); }
            catch (OperationCanceledException) { break; }
            catch (SocketException ex)
            {
                logger.LogWarning(ex, "The prod mirror's catcher couldn't accept a connection");
                continue;
            }
            _ = Task.Run(() => ServeAsync(client, stopping), CancellationToken.None);
        }
    }

    public void Dispose()
    {
        listener.Stop();
        authority.Dispose();
    }

    // ------------------------------------------------------------------ one connection

    async Task ServeAsync(TcpClient client, CancellationToken stopping)
    {
        using var _ = client;
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        idle.CancelAfter(Idle);
        var ct = idle.Token;
        try
        {
            var wire = new Wire(client.GetStream());
            var head = await wire.ReadHeadAsync(ct);
            if (head == null) return;
            var request = Head.Parse(head);

            if (request.Method == "CONNECT")
            {
                var (host, port) = HostAndPort(request.Target, 443);
                var passes = await mock.PassesAsync(host);
                if (passes == null)
                {
                    await wire.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n", ct);
                    await using var tls = new SslStream(wire.AsStream());
                    try
                    {
                        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                        {
                            ServerCertificate = authority.For(host),
                            ApplicationProtocols = [SslApplicationProtocol.Http11],
                        }, ct);
                    }
                    catch (Exception ex) when (ex is AuthenticationException or IOException)
                    {
                        // Most often the adapter doesn't trust the catcher's authority: said where the page shows it.
                        mock.Refused(host, ex.InnerException?.Message ?? ex.Message);
                        logger.LogDebug(ex, "An adapter's TLS to the prod mirror's catcher failed for {Host}", host);
                        return;
                    }
                    if (!await AnswerAsync(new Wire(tls), "https", port == 443 ? host : $"{host}:{port}", null, idle, ct))
                        // Some TLS stacks finish the handshake and only then check the certificate, and hang up.
                        mock.Refused(host, "it closed the connection without asking anything, which is how most adapters refuse a certificate they don't trust");
                }
                else
                {
                    mock.Passed("CONNECT", host, "", passes, "An adapter's process");
                    using var onwardClient = await ConnectAsync(host, port, ct);
                    await wire.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n", ct);
                    await PipeAsync(wire, onwardClient.GetStream(), ct);
                }
                return;
            }

            if (!request.Target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || !Uri.TryCreate(request.Target, UriKind.Absolute, out var url))
            {
                await wire.WriteAsync("HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", ct);
                return;
            }
            if (await mock.PassesAsync(url.Host) is not { } why)
            {
                await AnswerAsync(wire, "http", url.Authority, request, idle, ct);
                return;
            }
            mock.Passed(request.Method, url.Host, url.AbsolutePath, why, "An adapter's process");

            // Passed on as it came, one request to the connection, which the far side closes after answering.
            using var server = await ConnectAsync(url.Host, url.Port, ct);
            var serverStream = server.GetStream();
            await serverStream.WriteAsync(Encoding.Latin1.GetBytes(request.Rewritten(url.PathAndQuery) + "\r\n\r\n"), ct);
            await PipeAsync(wire, serverStream, ct);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or InvalidDataException or FormatException)
        {
            logger.LogDebug(ex, "A connection to the prod mirror's catcher ended early");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The prod mirror's catcher failed on a connection");
        }
    }

    /// <summary>Requests on one connection, each answered by the mock, until the client closes it. False when it closed before asking anything.</summary>
    async Task<bool> AnswerAsync(Wire wire, string scheme, string authorityPart, Head first, CancellationTokenSource idle, CancellationToken ct)
    {
        var request = first;
        var answered = false;
        while (true)
        {
            if (request == null)
            {
                var head = await wire.ReadHeadAsync(ct);
                if (head == null) return answered;
                request = Head.Parse(head);
            }
            if (request.ExpectsContinue) await wire.WriteAsync("HTTP/1.1 100 Continue\r\n\r\n", ct);
            var length = request.ContentLength ?? 0;
            if (length > MaxBody) throw new InvalidDataException("A body larger than the catcher reads.");
            var body = request.Chunked ? await wire.ReadChunkedAsync(MaxBody, ct) : length > 0 ? await wire.ReadBytesAsync((int)length, ct) : [];
            // Only a target that names its scheme is absolute: on Linux and macOS a path such as
            // /v2/labels would otherwise read as a file's address.
            var url = request.Target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || request.Target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(request.Target)
                : new Uri($"{scheme}://{authorityPart}{request.Target}");
            var answer = await mock.AnswerAsync(new MirrorRequest(request.Method, url, request.ContentType, body, "An adapter's process"));
            // Closed without a word, as a call that never got through.
            if (answer.Fail) return true;

            var reply = new StringBuilder()
                .Append("HTTP/1.1 ").Append(answer.Status).Append(' ').Append(Reason(answer.Status)).Append("\r\n")
                .Append("Content-Length: ").Append(answer.Body.Length).Append("\r\n");
            if (answer.ContentType != null) reply.Append("Content-Type: ").Append(answer.ContentType).Append("\r\n");
            // Plain HTTP comes to the catcher itself, and a client sends its next call to any host down the
            // same connection: each is answered on its own, so the next can be passed on if it isn't caught.
            var keepAlive = request.KeepAlive && scheme == "https";
            reply.Append("Connection: ").Append(keepAlive ? "keep-alive" : "close").Append("\r\n\r\n");
            await wire.WriteAsync(reply.ToString(), ct);
            if (request.Method != "HEAD") await wire.Stream.WriteAsync(answer.Body, ct);
            await wire.Stream.FlushAsync(ct);
            answered = true;
            if (!keepAlive) return true;
            idle.CancelAfter(Idle);
            request = null;
        }
    }

    static string Reason(int status) => status switch
    {
        200 => "OK", 201 => "Created", 202 => "Accepted", 204 => "No Content", 400 => "Bad Request", 401 => "Unauthorized",
        403 => "Forbidden", 404 => "Not Found", 409 => "Conflict", 422 => "Unprocessable Entity", 429 => "Too Many Requests",
        500 => "Internal Server Error", 502 => "Bad Gateway", 503 => "Service Unavailable",
        _ => "Answer",
    };

    /// <summary>A connection to where a passed-on call goes: straight there, or through Bitween's own proxy when it has one for that address.</summary>
    async Task<TcpClient> ConnectAsync(string host, int port, CancellationToken ct)
    {
        var target = new Uri($"https://{(host.Contains(':') ? $"[{host}]" : host)}:{port}");
        var via = onward != null && !onward.IsBypassed(target) ? onward.GetProxy(target) : null;
        var client = new TcpClient();
        try
        {
            if (via == null || via == target)
            {
                await client.ConnectAsync(host, port, ct);
                return client;
            }
            await client.ConnectAsync(via.Host, via.Port, ct);
            var wire = new Wire(client.GetStream());
            await wire.WriteAsync($"CONNECT {target.Authority} HTTP/1.1\r\nHost: {target.Authority}\r\n\r\n", ct);
            var answer = await wire.ReadHeadAsync(ct);
            if (answer == null || !answer.StartsWith("HTTP/1.1 200", StringComparison.Ordinal) && !answer.StartsWith("HTTP/1.0 200", StringComparison.Ordinal))
                throw new IOException($"Bitween's proxy wouldn't connect to {target.Authority}.");
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>Both ways at once, until either side closes.</summary>
    static async Task PipeAsync(Wire client, Stream far, CancellationToken ct)
    {
        using var done = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var up = client.PipeToAsync(far, done.Token);
        var down = far.CopyToAsync(client.Stream, done.Token);
        await Task.WhenAny(up, down);
        await done.CancelAsync();
        try { await Task.WhenAll(up, down); }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException) { }
    }

    static (string Host, int Port) HostAndPort(string authorityPart, int defaultPort)
    {
        if (authorityPart.StartsWith('['))
        {
            var close = authorityPart.IndexOf(']');
            var host6 = authorityPart[1..close];
            return (host6, authorityPart.Length > close + 2 && int.TryParse(authorityPart[(close + 2)..], out var p6) ? p6 : defaultPort);
        }
        var colon = authorityPart.LastIndexOf(':');
        return colon > 0 && int.TryParse(authorityPart[(colon + 1)..], out var port) ? (authorityPart[..colon], port) : (authorityPart, defaultPort);
    }

    // ------------------------------------------------------------------ HTTP/1.1, as much as a catcher needs

    sealed class Head
    {
        public string Method { get; private init; }
        public string Target { get; private init; }
        public List<(string Name, string Value)> Headers { get; } = [];

        string Header(string name) => Headers.LastOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)).Value;

        public long? ContentLength => long.TryParse(Header("Content-Length"), out var n) ? n : null;
        public bool Chunked => Header("Transfer-Encoding")?.Contains("chunked", StringComparison.OrdinalIgnoreCase) == true;
        public bool ExpectsContinue => string.Equals(Header("Expect"), "100-continue", StringComparison.OrdinalIgnoreCase);
        public string ContentType => Header("Content-Type");
        public bool KeepAlive => !string.Equals(Header("Connection") ?? Header("Proxy-Connection"), "close", StringComparison.OrdinalIgnoreCase);

        public static Head Parse(string head)
        {
            var lines = head.Split("\r\n");
            var first = lines[0].Split(' ');
            if (first.Length < 3) throw new InvalidDataException("Not an HTTP request.");
            var parsed = new Head { Method = first[0].ToUpperInvariant(), Target = first[1] };
            foreach (var line in lines.Skip(1))
            {
                var colon = line.IndexOf(':');
                if (colon > 0) parsed.Headers.Add((line[..colon].Trim(), line[(colon + 1)..].Trim()));
            }
            return parsed;
        }

        /// <summary>The request as a server is sent it: its own path, its proxy headers gone, the connection closed after.</summary>
        public string Rewritten(string pathAndQuery) =>
            string.Join("\r\n", new[] { $"{Method} {pathAndQuery} HTTP/1.1" }
                .Concat(Headers.Where(h => !h.Name.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase) &&
                                           !h.Name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) &&
                                           !h.Name.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                    .Select(h => $"{h.Name}: {h.Value}"))
                .Append("Connection: close"));
    }

    /// <summary>A stream read a head, a line or a body at a time, with what it read ahead kept for the next read.</summary>
    sealed class Wire(Stream stream)
    {
        readonly byte[] buffer = new byte[MaxHead];
        int start, end;

        public Stream Stream => stream;

        public Task WriteAsync(string text, CancellationToken ct) => stream.WriteAsync(Encoding.Latin1.GetBytes(text), ct).AsTask();

        async Task<bool> FillAsync(CancellationToken ct)
        {
            if (start == end) start = end = 0;
            if (end == buffer.Length)
            {
                if (start == 0) throw new InvalidDataException("A head larger than the catcher reads.");
                Buffer.BlockCopy(buffer, start, buffer, 0, end - start);
                end -= start;
                start = 0;
            }
            var read = await stream.ReadAsync(buffer.AsMemory(end), ct);
            end += read;
            return read > 0;
        }

        /// <summary>Up to the blank line that ends a head; null when the other side closed first.</summary>
        public async Task<string> ReadHeadAsync(CancellationToken ct)
        {
            while (true)
            {
                var at = buffer.AsSpan(start, end - start).IndexOf("\r\n\r\n"u8);
                if (at >= 0)
                {
                    var head = Encoding.Latin1.GetString(buffer, start, at);
                    start += at + 4;
                    return head;
                }
                if (!await FillAsync(ct)) return null;
            }
        }

        async Task<string> ReadLineAsync(CancellationToken ct)
        {
            while (true)
            {
                var at = buffer.AsSpan(start, end - start).IndexOf("\r\n"u8);
                if (at >= 0)
                {
                    var line = Encoding.Latin1.GetString(buffer, start, at);
                    start += at + 2;
                    return line;
                }
                if (!await FillAsync(ct)) throw new IOException("The connection closed mid-request.");
            }
        }

        public async Task<byte[]> ReadBytesAsync(int count, CancellationToken ct)
        {
            var result = new byte[count];
            var have = 0;
            while (have < count)
            {
                if (start == end && !await FillAsync(ct)) throw new IOException("The connection closed mid-body.");
                var take = Math.Min(count - have, end - start);
                Buffer.BlockCopy(buffer, start, result, have, take);
                start += take;
                have += take;
            }
            return result;
        }

        public async Task<byte[]> ReadChunkedAsync(int max, CancellationToken ct)
        {
            using var body = new MemoryStream();
            while (true)
            {
                var size = Convert.ToInt32((await ReadLineAsync(ct)).Split(';')[0].Trim(), 16);
                if (size == 0) break;
                if (body.Length + size > max) throw new InvalidDataException("A body larger than the catcher reads.");
                body.Write(await ReadBytesAsync(size, ct));
                await ReadLineAsync(ct);
            }
            while ((await ReadLineAsync(ct)).Length > 0) { }
            return body.ToArray();
        }

        /// <summary>The stream, read from what was read ahead first.</summary>
        public Stream AsStream() => end > start ? new Ahead(this) : stream;

        sealed class Ahead(Wire wire) : Stream
        {
            public override bool CanRead => true;
            public override bool CanWrite => true;
            public override bool CanSeek => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override async ValueTask<int> ReadAsync(Memory<byte> into, CancellationToken ct = default)
            {
                if (wire.end <= wire.start) return await wire.Stream.ReadAsync(into, ct);
                var take = Math.Min(into.Length, wire.end - wire.start);
                wire.buffer.AsMemory(wire.start, take).CopyTo(into);
                wire.start += take;
                return take;
            }

            public override int Read(byte[] into, int offset, int count) => ReadAsync(into.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> from, CancellationToken ct = default) => wire.Stream.WriteAsync(from, ct);
            public override void Write(byte[] from, int offset, int count) => wire.Stream.Write(from, offset, count);
            public override Task FlushAsync(CancellationToken ct) => wire.Stream.FlushAsync(ct);
            public override void Flush() => wire.Stream.Flush();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }

        /// <summary>What was read ahead, then the rest of the stream, to <paramref name="to"/>.</summary>
        public async Task PipeToAsync(Stream to, CancellationToken ct)
        {
            if (end > start)
            {
                await to.WriteAsync(buffer.AsMemory(start, end - start), ct);
                start = end = 0;
            }
            await stream.CopyToAsync(to, ct);
        }
    }
}

/// <summary>Serves <see cref="MirrorCatcher"/> once the mirror's mock exists, and sends connections' token requests through it too.</summary>
public sealed class MirrorCatcherService(MirrorCatcher catcher, MirrorMock mock, ILogger<MirrorCatcher> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ConnectionTokens.CatchWith(new MirrorCatchingHandler(mock) { InnerHandler = new SocketsHttpHandler() });
        OpenIdGatewayIssuers.CatchWith(new MirrorCatchingHandler(mock) { InnerHandler = new SocketsHttpHandler() });
        return catcher.RunAsync(mock, logger, stoppingToken);
    }
}
