using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SW.Bitween.UnitTests;

/// <summary>
/// Minimal in-process POP3 server used to exercise the real MailKit/Rebex clients
/// against USER/PASS/STAT/LIST/RETR/DELE/QUIT without touching a real mailbox.
/// </summary>
public sealed class FakePop3Server : IDisposable
{
    private readonly TcpListener _listener;
    private readonly List<string> _messages;
    private readonly HashSet<int> _deleted = new();
    private readonly string _expectedUser;
    private readonly string _expectedPassword;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptTask;

    public int Port { get; }
    public IReadOnlyCollection<int> DeletedMessageNumbers => _deleted;

    public FakePop3Server(IEnumerable<string> messages, string expectedUser = "user", string expectedPassword = "pass")
    {
        _messages = messages.ToList();
        _expectedUser = expectedUser;
        _expectedPassword = expectedPassword;

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _acceptTask = AcceptLoop(_cts.Token);
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                _ = HandleClient(client, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        await using var stream = client.GetStream();
        var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        var writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };

        await writer.WriteLineAsync("+OK Fake POP3 server ready");

        var authenticatedUser = string.Empty;

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;

            var spaceIndex = line.IndexOf(' ');
            var command = (spaceIndex < 0 ? line : line[..spaceIndex]).ToUpperInvariant();
            var argument = spaceIndex < 0 ? string.Empty : line[(spaceIndex + 1)..];

            switch (command)
            {
                case "CAPA":
                    await writer.WriteLineAsync("-ERR capabilities not supported");
                    break;

                case "USER":
                    authenticatedUser = argument;
                    await writer.WriteLineAsync("+OK");
                    break;

                case "PASS":
                    if (authenticatedUser == _expectedUser && argument == _expectedPassword)
                        await writer.WriteLineAsync("+OK logged in");
                    else
                        await writer.WriteLineAsync("-ERR invalid credentials");
                    break;

                case "STAT":
                    var activeCount = _messages.Count - _deleted.Count;
                    var totalSize = ActiveIndexes().Sum(i => Encoding.ASCII.GetByteCount(_messages[i]));
                    await writer.WriteLineAsync($"+OK {activeCount} {totalSize}");
                    break;

                case "LIST":
                    await writer.WriteLineAsync($"+OK {_messages.Count - _deleted.Count} messages");
                    foreach (var i in ActiveIndexes())
                        await writer.WriteLineAsync($"{i + 1} {Encoding.ASCII.GetByteCount(_messages[i])}");
                    await writer.WriteLineAsync(".");
                    break;

                case "RETR":
                    await HandleRetr(writer, argument);
                    break;

                case "DELE":
                    if (int.TryParse(argument, out var delNum) && delNum >= 1 && delNum <= _messages.Count)
                    {
                        _deleted.Add(delNum);
                        await writer.WriteLineAsync("+OK marked for deletion");
                    }
                    else
                    {
                        await writer.WriteLineAsync("-ERR no such message");
                    }
                    break;

                case "NOOP":
                    await writer.WriteLineAsync("+OK");
                    break;

                case "QUIT":
                    await writer.WriteLineAsync("+OK bye");
                    return;

                default:
                    await writer.WriteLineAsync("-ERR unknown command");
                    break;
            }
        }
    }

    private IEnumerable<int> ActiveIndexes() =>
        Enumerable.Range(0, _messages.Count).Where(i => !_deleted.Contains(i + 1));

    private async Task HandleRetr(StreamWriter writer, string argument)
    {
        if (!int.TryParse(argument, out var num) || num < 1 || num > _messages.Count || _deleted.Contains(num))
        {
            await writer.WriteLineAsync("-ERR no such message");
            return;
        }

        var message = _messages[num - 1];
        await writer.WriteLineAsync($"+OK {Encoding.ASCII.GetByteCount(message)} octets");

        foreach (var messageLine in message.Replace("\r\n", "\n").Split('\n'))
        {
            var stuffed = messageLine.StartsWith('.') ? "." + messageLine : messageLine;
            await writer.WriteLineAsync(stuffed);
        }

        await writer.WriteLineAsync(".");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
    }
}
