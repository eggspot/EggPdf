using System.Net;
using EggPdf.Core.Resources;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Core;

/// <summary>
/// <see cref="IpAddressRanges.IsPubliclyRoutable"/> decides which addresses the safe HTTP resolver may
/// connect to: loopback, private, link-local (cloud metadata), CGNAT, multicast and reserved ranges are
/// refused, including when they hide inside an IPv6 address (IPv4-mapped, NAT64, 6to4).
/// </summary>
public class IpAddressRangesTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]   // AWS/GCP/Azure instance metadata
    [InlineData("100.64.0.1")]        // CGNAT
    [InlineData("100.100.100.200")]   // Alibaba Cloud metadata (inside CGNAT)
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    [InlineData("192.0.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.7")]
    [InlineData("203.0.113.9")]
    [InlineData("224.0.0.1")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd00:ec2::254")]      // AWS IPv6 instance metadata
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]   // IPv4-mapped loopback
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::a9fe:a9fe")] // NAT64 of 169.254.169.254
    [InlineData("2002:a9fe:a9fe::1")]  // 6to4 of 169.254.169.254
    [InlineData("2001:db8::1")]        // documentation
    [InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2")] // Teredo
    [InlineData("::127.0.0.1")]        // deprecated IPv4-compatible
    public void IsPubliclyRoutable_NonPublicAddress_ReturnsFalse(string address)
    {
        IpAddressRanges.IsPubliclyRoutable(IPAddress.Parse(address)).Should().BeFalse(address);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.1")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("2a00:1450:4001:80b::200e")]
    [InlineData("::ffff:8.8.8.8")]
    [InlineData("64:ff9b::808:808")]   // NAT64 of 8.8.8.8
    [InlineData("2002:808:808::1")]    // 6to4 of 8.8.8.8
    public void IsPubliclyRoutable_PublicAddress_ReturnsTrue(string address)
    {
        IpAddressRanges.IsPubliclyRoutable(IPAddress.Parse(address)).Should().BeTrue(address);
    }
}
