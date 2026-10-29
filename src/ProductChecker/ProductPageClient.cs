using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ProductChecker;

public sealed class ProductPageClient : IDisposable
{
    private readonly HttpClient client;
    private readonly HttpClient proxyClient;
    private readonly HashSet<string> proxyHosts;
    public ProductPageClient()
    {
        // An administrator can authorize exact store hosts for a trusted egress proxy.
        // Never send arbitrary user URLs through a proxy that resolves DNS for us.
        proxyHosts = (Environment.GetEnvironmentVariable("PRODUCTCHECKER_STORE_HOSTS") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        proxyClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All
        }) { Timeout = TimeSpan.FromSeconds(15) };
        proxyClient.DefaultRequestHeaders.UserAgent.ParseAdd("ProductChecker/0.1");
        client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
                if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
                    throw new HttpRequestException("The hostname must resolve only to public addresses.");
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    // Connect to the validated addresses, preventing a second DNS lookup.
                    await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            }
        }) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ProductChecker/0.1");
    }

    public static bool IsAllowedUrl(Uri uri) => uri.Scheme == "https" && uri.Port == 443
        && uri.UserInfo.Length == 0 && uri.Host.Length > 0 && uri.AbsoluteUri.Length <= 4096
        && (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) || IsPublicAddress(address));

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        var b = address.GetAddressBytes();
        if (b.Length == 16)
            return (b[0] & 0xe0) == 0x20 && !(b[0] == 0x20 && b[1] == 0x01 && b[2] <= 0x01)
                && !(b[0] == 0x20 && b[1] == 0x02) // Disallow transition networks.
                && !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8);
        return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224
            && !(b[0] == 100 && b[1] is >= 64 and <= 127)
            && !(b[0] == 169 && b[1] == 254)
            && !(b[0] == 172 && b[1] is >= 16 and <= 31)
            && !(b[0] == 192 && (b[1] == 168 || b[1] == 0 || (b[1] == 88 && b[2] == 99)))
            && !(b[0] == 198 && (b[1] is 18 or 19 || (b[1] == 51 && b[2] == 100)))
            && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
    }

    public async Task<string> FetchAsync(Uri uri, CancellationToken cancellationToken)
    {
        var useProxy = !HttpClient.DefaultProxy.IsBypassed(uri);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        for (var redirects = 0; redirects <= 3; redirects++)
        {
            if (!IsAllowedUrl(uri)) throw new HttpRequestException("Unsupported destination.");
            if (useProxy && (!proxyHosts.Contains(uri.IdnHost) || HttpClient.DefaultProxy.IsBypassed(uri)))
                throw new HttpRequestException("The store must be authorized for the configured proxy.");
            using var response = await (useProxy ? proxyClient : client).GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location ?? throw new HttpRequestException("Missing redirect destination.");
                uri = new Uri(uri, location);
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentType?.MediaType is not ("text/html" or "application/xhtml+xml"))
                throw new HttpRequestException("Expected an HTML product page.");
            const int maxBytes = 2 * 1024 * 1024;
            if (response.Content.Headers.ContentLength > maxBytes) throw new HttpRequestException("Page is too large.");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, deadline.Token)) > 0)
            {
                if (output.Length + count > maxBytes) throw new HttpRequestException("Page is too large.");
                output.Write(buffer, 0, count);
            }
            return Encoding.UTF8.GetString(output.ToArray());
        }
        throw new HttpRequestException("Too many redirects.");
    }

    public void Dispose() { client.Dispose(); proxyClient.Dispose(); }
}
