using System.Net;
using EggPdf.Core.Resources;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Core;

public class IpAddressRangesTests
{
    [Theory]
    [InlineData("127.0.0.1", "loopback")]
    [InlineData("127.255.255.254", "loopback")]
    [InlineData("10.0.0.1", "private")]
    [InlineData("172.16.0.1", "private")]
    [InlineData("172.31.255.255", "private")]
    [InlineData("192.168.1.1", "private")]
    [InlineData("169.254.169.254", "link-local: the AWS/GCP/Azure instance metadata address")]
    [InlineData("100.64.0.1", "CGNAT")]
    [InlineData("100.100.100.200", "the Alibaba Cloud metadata address, inside CGNAT")]
    [InlineData("0.0.0.0", "this network")]
    [InlineData("0.1.2.3", "this network")]
    [InlineData("192.0.0.1", "IETF protocol assignments")]
    [InlineData("192.0.2.1", "documentation")]
    [InlineData("198.18.0.1", "benchmarking")]
    [InlineData("198.51.100.7", "documentation")]
    [InlineData("203.0.113.9", "documentation")]
    [InlineData("224.0.0.1", "multicast")]
    [InlineData("240.0.0.1", "reserved")]
    [InlineData("255.255.255.255", "broadcast")]
    [InlineData("::", "unspecified")]
    [InlineData("::1", "IPv6 loopback")]
    [InlineData("fe80::1", "IPv6 link-local")]
    [InlineData("fc00::1", "IPv6 unique local")]
    [InlineData("fd00:ec2::254", "the AWS IPv6 instance metadata address")]
    [InlineData("ff02::1", "IPv6 multicast")]
    [InlineData("::ffff:127.0.0.1", "IPv4-mapped loopback")]
    [InlineData("::ffff:169.254.169.254", "IPv4-mapped metadata address")]
    [InlineData("64:ff9b::a9fe:a9fe", "NAT64 of 169.254.169.254")]
    [InlineData("2002:a9fe:a9fe::1", "6to4 of 169.254.169.254")]
    [InlineData("2001:db8::1", "IPv6 documentation")]
    [InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2", "Teredo")]
    [InlineData("::127.0.0.1", "deprecated IPv4-compatible")]
    public void IsPubliclyRoutable_NonPublicAddress_ReturnsFalse(string address, string range)
    {
        IpAddressRanges.IsPubliclyRoutable(IPAddress.Parse(address)).Should().BeFalse(
            $"{address} is {range}; the safe HTTP resolver must not connect to it, even hidden inside an IPv6 address");
    }

    [Theory]
    [InlineData("8.8.8.8", "public")]
    [InlineData("1.1.1.1", "public")]
    [InlineData("172.15.255.255", "just below the 172.16/12 private range")]
    [InlineData("172.32.0.1", "just above the 172.16/12 private range")]
    [InlineData("100.63.255.255", "just below CGNAT")]
    [InlineData("100.128.0.1", "just above CGNAT")]
    [InlineData("2606:4700:4700::1111", "public IPv6")]
    [InlineData("2a00:1450:4001:80b::200e", "public IPv6")]
    [InlineData("::ffff:8.8.8.8", "IPv4-mapped public")]
    [InlineData("64:ff9b::808:808", "NAT64 of 8.8.8.8")]
    [InlineData("2002:808:808::1", "6to4 of 8.8.8.8")]
    public void IsPubliclyRoutable_PublicAddress_ReturnsTrue(string address, string range)
    {
        IpAddressRanges.IsPubliclyRoutable(IPAddress.Parse(address)).Should().BeTrue($"{address} is {range}");
    }
}
