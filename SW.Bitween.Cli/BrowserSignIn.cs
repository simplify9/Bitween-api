using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace SW.Bitween.Cli;

/// <summary>
/// Signing in through the browser, so any account can sign in however it signs in to Bitween,
/// Microsoft included, with nothing to configure. The admin UI asks the member to confirm, then
/// hands a one-time code back to a listener on this machine's loopback; the code is only good with
/// the verifier this process keeps (PKCE). Without a browser on this machine, the page shows the
/// code to paste instead.
/// </summary>
public static class BrowserSignIn
{
    static readonly TimeSpan Wait = TimeSpan.FromMinutes(5);

    /// <summary>For tests: opens the address instead of a browser. Returns whether it could.</summary>
    internal static Func<string, bool> OpenOverride;

    public static async Task<Profile> SignInAsync(string url, bool insecure, bool openBrowser, TextReader input)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var page = $"{url.TrimEnd('/')}/cli-login?challenge={challenge}&state={state}";

        string code;
        if (openBrowser)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var address = $"{page}&port={port}";

            Console.WriteLine("Opening your browser to sign in. If it doesn't open, go to:");
            Console.WriteLine($"  {address}");
            if (!Open(address))
                Console.WriteLine("(Couldn't open a browser here; open the address above on this machine.)");
            Console.WriteLine("Waiting for you to confirm in the browser...");

            code = await CallbackAsync(listener, state);
        }
        else
        {
            Console.WriteLine("Open this address in a browser, sign in, and confirm:");
            Console.WriteLine($"  {page}");
            Console.Write("Then paste the code it shows: ");
            code = input.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(code)) throw new BitweenApiException("No code was given.");
        }

        return await BitweenApi.RedeemAsync(url, code, verifier, insecure);
    }

    /// <summary>Waits for the browser's redirect back here, and answers it with a page to close.</summary>
    static async Task<string> CallbackAsync(TcpListener listener, string state)
    {
        using var timeout = new CancellationTokenSource(Wait);
        while (true)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new BitweenApiException($"Nothing came back from the browser within {Wait.TotalMinutes} minutes; run bitween login again.");
            }

            using (client)
            {
                var stream = client.GetStream();
                var line = await ReadRequestLineAsync(stream, timeout.Token);
                // "GET /callback?code=...&state=... HTTP/1.1"; anything else (a favicon) gets a 404.
                var target = line?.Split(' ') is { Length: >= 2 } parts && parts[0] == "GET" ? parts[1] : null;
                if (target == null || !target.StartsWith("/callback", StringComparison.Ordinal))
                {
                    await RespondAsync(stream, 404, "Not found.");
                    continue;
                }

                var query = HttpUtility.ParseQueryString(target.Contains('?') ? target[(target.IndexOf('?') + 1)..] : "");
                if (query["state"] != state)
                {
                    // Not the sign-in this process started: ignored, still waiting for that one.
                    await RespondAsync(stream, 400, "This sign-in wasn't started by the bitween CLI waiting here.");
                    continue;
                }
                if (!string.IsNullOrEmpty(query["error"]))
                {
                    await RespondAsync(stream, 200, "Sign-in cancelled. You can close this tab.");
                    throw new BitweenApiException("Sign-in was cancelled in the browser.");
                }

                await RespondAsync(stream, 200, "The bitween CLI is signed in. You can close this tab.");
                return query["code"] ?? throw new BitweenApiException("The browser sent no code back.");
            }
        }
    }

    static async Task<string> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancellation)
    {
        var buffer = new byte[8192];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), cancellation);
            if (n == 0) break;
            read += n;
            var text = Encoding.ASCII.GetString(buffer, 0, read);
            // The whole head, so the browser isn't answered before it has finished asking.
            if (text.Contains("\r\n\r\n")) return text[..text.IndexOf("\r\n", StringComparison.Ordinal)];
        }
        return null;
    }

    static async Task RespondAsync(NetworkStream stream, int status, string message)
    {
        var body = Encoding.UTF8.GetBytes(
            "<!doctype html><meta charset=\"utf-8\"><title>bitween CLI</title>" +
            "<body style=\"font-family:system-ui,sans-serif;margin:4rem auto;max-width:32rem;text-align:center\">" +
            $"<p>{WebUtility.HtmlEncode(message)}</p></body>");
        var reason = status switch { 200 => "OK", 400 => "Bad Request", _ => "Not Found" };
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head);
        await stream.WriteAsync(body);
    }

    static bool Open(string address)
    {
        if (OpenOverride != null) return OpenOverride(address);
        try
        {
            var start = OperatingSystem.IsWindows() ? new ProcessStartInfo(address) { UseShellExecute = true }
                : OperatingSystem.IsMacOS() ? new ProcessStartInfo("open", [address])
                : new ProcessStartInfo("xdg-open", [address]);
            start.RedirectStandardError = !OperatingSystem.IsWindows();
            start.RedirectStandardOutput = !OperatingSystem.IsWindows();
            using var process = Process.Start(start);
            return process != null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
