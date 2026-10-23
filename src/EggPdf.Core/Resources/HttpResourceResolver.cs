using System;
using System.IO;
using System.Net;
using System.Net.Http;
#if NET6_0_OR_GREATER
using System.Net.Sockets;
#endif
using System.Threading;
using System.Threading.Tasks;

namespace EggPdf.Core.Resources;

/// <summary>
/// Resolves http:// and https:// URLs, safe for untrusted HTML by default: loopback, private, link-local
/// (incl. the 169.254.169.254 cloud metadata endpoint) and other non-public addresses are refused unless
/// <see cref="HttpResourceOptions.AllowPrivateNetworks"/> is set; every redirect hop is re-checked (scheme,
/// <see cref="HttpResourceOptions.AllowedDomains"/>, address) up to <see cref="HttpResourceOptions.MaxRedirects"/>;
/// the body is capped at <see cref="HttpResourceOptions.MaxResponseSizeBytes"/> while streaming; and the whole
/// fetch, redirects included, is bounded by <see cref="HttpResourceOptions.TimeoutSeconds"/>. Returns null for
/// anything refused or failed; throws only when the caller's token is cancelled. Thread-safe.
/// </summary>
/// <remarks>
/// On .NET 6+ the address is checked when the connection is made, so a hostname cannot be re-pointed
/// (DNS rebinding) between check and connect; on .NET Standard it is checked by resolving the hostname
/// just before each request. A caller-supplied <see cref="HttpClient"/> keeps its own handler: addresses
/// are then checked by resolving before each request, and redirects are only re-checked if that client
/// does not follow them itself (AllowAutoRedirect = false).
/// </remarks>
public class HttpResourceResolver : IResourceResolver
{
    private readonly HttpClient _httpClient;
    private readonly HttpResourceOptions _options;
    private readonly bool _checksAddressOnConnect;

    /// <summary>Test seam: replaces the public-address policy when set.</summary>
    internal Func<IPAddress, bool>? AddressFilter { get; set; }

    /// <summary>Creates a resolver; with no <paramref name="httpClient"/> it uses its own hardened handler.</summary>
    public HttpResourceResolver(HttpResourceOptions? options = null, HttpClient? httpClient = null)
    {
        _options = options ?? new HttpResourceOptions();
        if (httpClient != null)
        {
            _httpClient = httpClient;
            return;
        }

#if NET6_0_OR_GREATER
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = ConnectToAllowedAddressAsync,
        };
        _checksAddressOnConnect = true;
#else
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false };
#endif
        _httpClient = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    /// <inheritdoc />
    public async Task<ResourceResult?> ResolveAsync(string url, ResourceType type, CancellationToken ct = default)
    {
        if (!TryParseHttpUrl(url, out var uri)) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        try
        {
            for (int redirects = 0; ; redirects++)
            {
                if (!IsDomainAllowed(uri)) return null;
                if (!_checksAddressOnConnect && !await ResolvesToAllowedAddressesAsync(uri.DnsSafeHost).ConfigureAwait(false))
                    return null;

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);

                if (IsRedirect(response.StatusCode))
                {
                    var location = response.Headers.Location;
                    if (location == null || redirects >= _options.MaxRedirects) return null;
                    if (!location.IsAbsoluteUri) location = new Uri(uri, location);
                    if (!IsHttp(location)) return null;
                    uri = location;
                    continue;
                }

                if (!response.IsSuccessStatusCode) return null;
                if (response.Content.Headers.ContentLength > _options.MaxResponseSizeBytes) return null;

                var bytes = await ReadCappedAsync(response.Content, _options.MaxResponseSizeBytes, timeout.Token).ConfigureAwait(false);
                if (bytes == null) return null;

                return new ResourceResult(bytes, response.Content.Headers.ContentType?.MediaType,
                    redirects == 0 ? url : uri.AbsoluteUri);
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null; // refused address, network failure, or our own timeout
        }
    }

    private bool IsAddressAllowed(IPAddress address) =>
        AddressFilter?.Invoke(address) ?? (_options.AllowPrivateNetworks || IpAddressRanges.IsPubliclyRoutable(address));

    private async Task<bool> ResolvesToAllowedAddressesAsync(string host)
    {
        IPAddress[] addresses;
        try { addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false); }
        catch (Exception) { return false; }
        return AllAllowed(addresses);
    }

    private bool AllAllowed(IPAddress[] addresses)
    {
        if (addresses.Length == 0) return false;
        // Every record must pass, so a hostname mixing a public and a private address is refused outright
        foreach (var address in addresses)
            if (!IsAddressAllowed(address)) return false;
        return true;
    }

#if NET6_0_OR_GREATER
    private async ValueTask<Stream> ConnectToAllowedAddressAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var endPoint = context.DnsEndPoint;
        var addresses = await Dns.GetHostAddressesAsync(endPoint.Host, ct).ConfigureAwait(false);
        if (!AllAllowed(addresses))
            throw new HttpRequestException($"Refused to connect to '{endPoint.Host}': not a public network address.");

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endPoint.Port), ct).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                socket.Dispose();
                lastError = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        throw lastError ?? new HttpRequestException($"Could not connect to '{endPoint.Host}'.");
    }
#endif

    private bool IsDomainAllowed(Uri uri)
    {
        var allowed = _options.AllowedDomains;
        if (allowed == null || allowed.Length == 0) return true;
        foreach (var domain in allowed)
            if (uri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, long maxBytes, CancellationToken ct)
    {
        using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static bool TryParseHttpUrl(string url, out Uri uri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out uri!) && IsHttp(uri)) return true;
        uri = null!;
        return false;
    }

    private static bool IsHttp(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    private static bool IsRedirect(HttpStatusCode status) =>
        status == HttpStatusCode.MovedPermanently || status == HttpStatusCode.Found ||
        status == HttpStatusCode.SeeOther || status == HttpStatusCode.TemporaryRedirect || (int)status == 308;
}

/// <summary>Limits for <see cref="HttpResourceResolver"/>.</summary>
public class HttpResourceOptions
{
    /// <summary>Host names that may be fetched (exact, case-insensitive); null or empty allows any host.</summary>
    public string[]? AllowedDomains { get; set; }

    /// <summary>Whole-fetch time limit in seconds, redirects included.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Largest response body accepted, in bytes; larger responses are discarded.</summary>
    public long MaxResponseSizeBytes { get; set; } = 50 * 1024 * 1024; // 50MB

    /// <summary>Redirects followed before giving up; each hop is re-checked like the original URL.</summary>
    public int MaxRedirects { get; set; } = 5;

    /// <summary>
    /// Allow loopback, private (10/8, 172.16/12, 192.168/16), link-local (incl. cloud metadata) and other
    /// non-public addresses. Off by default: turn on only when the HTML is trusted.
    /// </summary>
    public bool AllowPrivateNetworks { get; set; }

    /// <summary>User-Agent header sent with every request.</summary>
    public string UserAgent { get; set; } = "EggPdf/" + (typeof(HttpResourceOptions).Assembly.GetName().Version?.ToString(3) ?? "1");
}
