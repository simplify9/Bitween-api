using System.Net;
using System.Net.Sockets;

namespace SW.Bitween.NativeAdapters;

/// <summary>
/// Decides which addresses the HTTP adapters may connect to, checked after DNS resolution so a
/// hostname that points somewhere forbidden is caught as well as a literal address.
/// </summary>
/// <remarks>
/// <para>
/// Link-local addresses are always refused. That range holds the cloud metadata endpoints
/// (169.254.169.254, and fd00:ec2::254 on AWS IPv6) which hand out the node's own credentials,
/// and no partner integration lives there.
/// </para>
/// <para>
/// Private and loopback ranges are refused only when <c>blockPrivate</c> is set. Bitween is
/// self-hosted and calling systems inside the customer's own network is a normal integration, so
/// refusing them by default would break deployments that do exactly that.
/// </para>
/// </remarks>
internal static class OutboundAddressGuard
{
    private static readonly IPAddress AwsMetadataV6 = IPAddress.Parse("fd00:ec2::254");

    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> ConnectCallback(
        bool blockPrivate) => async (context, cancellationToken) =>
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);

        var allowed = addresses.Where(a => !IsBlocked(a, blockPrivate)).ToArray();
        if (allowed.Length == 0)
            throw new HttpRequestException(
                $"Bitween will not connect to {host}: it resolves only to addresses adapters may not reach " +
                "(link-local or cloud metadata" + (blockPrivate ? ", private or loopback" : "") + ").");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    };

    internal static bool IsBlocked(IPAddress address, bool blockPrivate)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.Equals(AwsMetadataV6) || address.IsIPv6LinkLocal)
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            if (b[0] == 169 && b[1] == 254) return true;
            if (!blockPrivate) return false;

            return b[0] == 0 ||
                   b[0] == 10 ||
                   b[0] == 127 ||
                   (b[0] == 100 && b[1] >= 64 && b[1] <= 127) ||
                   (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
                   (b[0] == 192 && b[1] == 168);
        }

        if (!blockPrivate) return false;

        var v6 = address.GetAddressBytes();
        return IPAddress.IsLoopback(address) ||
               address.Equals(IPAddress.IPv6Any) ||
               (v6[0] & 0xFE) == 0xFC; // fc00::/7, unique local
    }
}
