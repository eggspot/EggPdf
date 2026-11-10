using System.Net;
using System.Net.Sockets;

namespace EggPdf.Core.Resources;

/// <summary>
/// Classifies IP addresses for <see cref="HttpResourceResolver"/>: only publicly routable unicast
/// addresses may be fetched from untrusted HTML. Loopback, private (RFC 1918), link-local (incl. the
/// 169.254.169.254 cloud metadata endpoint), CGNAT, unique-local, multicast, documentation and reserved
/// ranges are refused, including when an IPv4 address hides inside IPv6 (mapped, NAT64, 6to4).
/// </summary>
internal static class IpAddressRanges
{
    /// <summary>
    /// IPv6: an IPv4 address embedded through ::ffff:a.b.c.d (mapped), 64:ff9b::a.b.c.d (NAT64 well-known prefix) or
    /// 2002:aabb:ccdd::/16 (6to4) is judged as IPv4. Otherwise only global unicast (2000::/3) is public, except
    /// 2001::/23 (IETF protocol assignments, incl. Teredo) and 2001:db8::/32 (documentation).
    /// </summary>
    public static bool IsPubliclyRoutable(IPAddress address)
    {
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork) return IsPublicV4(b, 0);
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return false;

        if (AllZero(b, 0, 10) && b[10] == 0xFF && b[11] == 0xFF) return IsPublicV4(b, 12);
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && AllZero(b, 4, 8)) return IsPublicV4(b, 12);
        if (b[0] == 0x20 && b[1] == 0x02) return IsPublicV4(b, 2);

        bool isGlobalUnicast = (b[0] & 0xE0) == 0x20;
        if (!isGlobalUnicast) return false;
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] <= 0x01) return false;
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return false;
        return true;
    }

    /// <summary>
    /// Refuses 0/8 ("this network"), 10/8 and 172.16/12 and 192.168/16 (private), 127/8 (loopback), 224/3 (multicast,
    /// reserved, broadcast), 100.64/10 (CGNAT), 169.254/16 (link-local, cloud metadata), 192.0.0/24 and 192.0.2/24
    /// (IETF assignments, TEST-NET-1), 192.88.99/24 (6to4 relay), 198.18/15 (benchmarking), 198.51.100/24 and
    /// 203.0.113/24 (TEST-NET-2 and -3).
    /// </summary>
    private static bool IsPublicV4(byte[] b, int i)
    {
        byte first = b[i], second = b[i + 1], third = b[i + 2];
        if (first == 0 || first == 10 || first == 127) return false;
        if (first >= 224) return false;
        if (first == 100 && second >= 64 && second <= 127) return false;
        if (first == 169 && second == 254) return false;
        if (first == 172 && second >= 16 && second <= 31) return false;
        if (first == 192 && second == 168) return false;
        if (first == 192 && second == 0 && (third == 0 || third == 2)) return false;
        if (first == 192 && second == 88 && third == 99) return false;
        if (first == 198 && (second == 18 || second == 19)) return false;
        if (first == 198 && second == 51 && third == 100) return false;
        if (first == 203 && second == 0 && third == 113) return false;
        return true;
    }

    private static bool AllZero(byte[] b, int start, int count)
    {
        for (int i = start; i < start + count; i++)
            if (b[i] != 0) return false;
        return true;
    }
}
