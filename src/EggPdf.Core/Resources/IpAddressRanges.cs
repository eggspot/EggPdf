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
    public static bool IsPubliclyRoutable(IPAddress address)
    {
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork) return IsPublicV4(b, 0);
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return false;

        // ::ffff:a.b.c.d (IPv4-mapped) and 64:ff9b::a.b.c.d (NAT64 well-known prefix) reach the embedded IPv4 address
        if (AllZero(b, 0, 10) && b[10] == 0xFF && b[11] == 0xFF) return IsPublicV4(b, 12);
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && AllZero(b, 4, 8)) return IsPublicV4(b, 12);
        // 2002:aabb:ccdd::/16 (6to4) tunnels to aa.bb.cc.dd
        if (b[0] == 0x20 && b[1] == 0x02) return IsPublicV4(b, 2);

        // Everything outside 2000::/3 (global unicast) is loopback, link-local, unique-local, multicast or reserved
        if ((b[0] & 0xE0) != 0x20) return false;
        // 2001::/23 (IETF protocol assignments, incl. Teredo 2001::/32) and 2001:db8::/32 (documentation)
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] <= 0x01) return false;
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return false;
        return true;
    }

    private static bool IsPublicV4(byte[] b, int i)
    {
        byte a = b[i], c = b[i + 1], d = b[i + 2];
        if (a == 0 || a == 10 || a == 127) return false;          // "this network", private, loopback
        if (a >= 224) return false;                                // multicast, reserved, broadcast
        if (a == 100 && c >= 64 && c <= 127) return false;         // 100.64.0.0/10 CGNAT
        if (a == 169 && c == 254) return false;                    // link-local, cloud metadata
        if (a == 172 && c >= 16 && c <= 31) return false;          // private
        if (a == 192 && c == 168) return false;                    // private
        if (a == 192 && c == 0 && (d == 0 || d == 2)) return false; // IETF protocol assignments, TEST-NET-1
        if (a == 192 && c == 88 && d == 99) return false;          // 6to4 relay anycast
        if (a == 198 && (c == 18 || c == 19)) return false;        // benchmarking
        if (a == 198 && c == 51 && d == 100) return false;         // TEST-NET-2
        if (a == 203 && c == 0 && d == 113) return false;          // TEST-NET-3
        return true;
    }

    private static bool AllZero(byte[] b, int start, int count)
    {
        for (int i = start; i < start + count; i++)
            if (b[i] != 0) return false;
        return true;
    }
}
