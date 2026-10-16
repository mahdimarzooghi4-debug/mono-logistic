using System.Net;
using System.Net.Sockets;

namespace Mono.Api.Domain;

public interface IWebhookDnsResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemWebhookDnsResolver : IWebhookDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

public static class WebhookPublicAddressPolicy
{
    private static readonly (byte[] Network, int PrefixLength)[] NonPublicV4 =
    [
        (new byte[] { 0, 0, 0, 0 }, 8),
        (new byte[] { 10, 0, 0, 0 }, 8),
        (new byte[] { 100, 64, 0, 0 }, 10),
        (new byte[] { 127, 0, 0, 0 }, 8),
        (new byte[] { 169, 254, 0, 0 }, 16),
        (new byte[] { 172, 16, 0, 0 }, 12),
        (new byte[] { 192, 0, 0, 0 }, 24),
        (new byte[] { 192, 0, 2, 0 }, 24),
        (new byte[] { 192, 88, 99, 0 }, 24),
        (new byte[] { 192, 168, 0, 0 }, 16),
        (new byte[] { 198, 18, 0, 0 }, 15),
        (new byte[] { 198, 51, 100, 0 }, 24),
        (new byte[] { 203, 0, 113, 0 }, 24),
        (new byte[] { 224, 0, 0, 0 }, 4),
        (new byte[] { 240, 0, 0, 0 }, 4)
    ];

    private static readonly (byte[] Network, int PrefixLength)[] NonPublicV6 =
    [
        (new byte[] { 0x00, 0x64, 0xff, 0x9b, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 96), // well-known NAT64
        (new byte[] { 0x00, 0x64, 0xff, 0x9b, 0x00, 0x01, 0x00, 0x00 }, 48), // local-use NAT64
        (new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 64), // discard-only
        (new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 }, 64), // dummy prefix
        (new byte[] { 0x20, 0x01, 0x00, 0x00 }, 23), // protocol assignments and special-purpose ranges
        (new byte[] { 0x20, 0x01, 0x00, 0x20 }, 28), // ORCHIDv2
        (new byte[] { 0x20, 0x01, 0x0d, 0xb8 }, 32), // documentation
        (new byte[] { 0x20, 0x02 }, 16),             // 6to4 embeds an IPv4 destination
        (new byte[] { 0x3f, 0xff, 0x00, 0x00 }, 20), // documentation
        (new byte[] { 0x5f, 0x00 }, 16)              // SRv6 SIDs are not globally reachable
    ];

    public static bool IsPublicInternetAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
            return !NonPublicV4.Any(range => IsInPrefix(bytes, range.Network, range.PrefixLength));

        if (bytes.Length != 16 || address.IsIPv6Any || address.IsIPv6Loopback ||
            address.IsIPv6LinkLocal || address.IsIPv6Multicast ||
            (bytes[0] & 0xfe) == 0xfc)
            return false;

        // Only global-unicast space is eligible; this excludes unspecified, ULA and transition scopes.
        if ((bytes[0] & 0xe0) != 0x20)
            return false;
        return !NonPublicV6.Any(range => IsInPrefix(bytes, range.Network, range.PrefixLength));
    }

    private static bool IsInPrefix(byte[] address, byte[] network, int prefixLength)
    {
        var wholeBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;
        if (!address.AsSpan(0, wholeBytes).SequenceEqual(network.AsSpan(0, wholeBytes)))
            return false;
        if (remainingBits == 0)
            return true;
        var mask = (byte)(0xff << (8 - remainingBits));
        return (address[wholeBytes] & mask) == (network[wholeBytes] & mask);
    }
}

public sealed class WebhookSafeSocketConnector(IWebhookDnsResolver dnsResolver)
{
    public async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;
        var addresses = await dnsResolver.ResolveAsync(host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(address => !WebhookPublicAddressPolicy.IsPublicInternetAddress(address)))
            throw new HttpRequestException("Webhook destination resolved to a non-public address.");

        Exception? lastError = null;
        foreach (var address in addresses.Distinct())
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                if (exception is OperationCanceledException)
                    throw;
                lastError = exception;
            }
        }

        throw new HttpRequestException("Could not connect to the validated webhook destination.", lastError);
    }
}

public static class WebhookDeliveryHttpClientFactory
{
    public static HttpClient Create(IWebhookDnsResolver dnsResolver)
    {
        var connector = new WebhookSafeSocketConnector(dnsResolver);
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 20,
            MaxResponseHeadersLength = 16,
            ConnectCallback = connector.ConnectAsync
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
    }
}
