using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EggPdf.Core.Resources;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Core;

/// <summary>
/// <see cref="HttpResourceResolver"/> is safe for untrusted HTML by default: it refuses loopback,
/// private and link-local addresses (checked when connecting, so DNS can't smuggle them in), re-checks
/// every redirect hop, and caps response size and time. Tests run against a real loopback server.
/// </summary>
public class HttpResourceResolverTests
{
    private static readonly byte[] Payload = Encoding.ASCII.GetBytes("body { color: red; }");

    private static LocalHttpServer Serve(byte[]? body = null) =>
        new(_ => new LocalHttpServer.Response { Body = body ?? Payload });

    private static HttpResourceResolver AllowLoopback(HttpResourceOptions? options = null)
    {
        options ??= new HttpResourceOptions();
        options.AllowPrivateNetworks = true;
        return new HttpResourceResolver(options);
    }

    [Fact]
    public async Task Resolve_LoopbackAddress_RefusedByDefault()
    {
        using var server = Serve();

        var result = await new HttpResourceResolver().ResolveAsync(server.Url("/a.css"), ResourceType.StyleSheet);

        result.Should().BeNull();
        server.Requests.Should().BeEmpty("the address is refused before any connection is made");
    }

    [Fact]
    public async Task Resolve_HostnameResolvingToLoopback_RefusedByDefault()
    {
        using var server = Serve();
        var url = "http://localhost:" + server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/a.css";

        var result = await new HttpResourceResolver().ResolveAsync(url, ResourceType.StyleSheet);

        result.Should().BeNull();
        server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_AllowPrivateNetworks_ReturnsBody()
    {
        using var server = Serve();

        var result = await AllowLoopback().ResolveAsync(server.Url("/a.css"), ResourceType.StyleSheet);

        result.Should().NotBeNull();
        result!.Data.Should().Equal(Payload);
        result.ResolvedUrl.Should().Be(server.Url("/a.css"));
    }

    [Theory]
    [InlineData("file:///etc/hosts")]
    [InlineData("ftp://example.com/a.css")]
    [InlineData("gopher://127.0.0.1:6379/_INFO")]
    [InlineData("data:text/css,a{}")]
    [InlineData("not a url")]
    public async Task Resolve_NonHttpUrl_ReturnsNull(string url)
    {
        var result = await AllowLoopback().ResolveAsync(url, ResourceType.StyleSheet);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_NotFound_ReturnsNull()
    {
        using var server = new LocalHttpServer(_ => new LocalHttpServer.Response { Status = 404 });

        var result = await AllowLoopback().ResolveAsync(server.Url("/missing.css"), ResourceType.StyleSheet);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_Redirect_FollowedAndReportsFinalUrl()
    {
        LocalHttpServer? server = null;
        server = new LocalHttpServer(path => path == "/a.css"
            ? new LocalHttpServer.Response { Status = 302, Location = "/b.css" }
            : new LocalHttpServer.Response { Body = Payload });
        using var _ = server;

        var result = await AllowLoopback().ResolveAsync(server.Url("/a.css"), ResourceType.StyleSheet);

        result.Should().NotBeNull();
        result!.Data.Should().Equal(Payload);
        result.ResolvedUrl.Should().Be(server.Url("/b.css"));
    }

    [Fact]
    public async Task Resolve_RedirectToFileScheme_Refused()
    {
        using var server = new LocalHttpServer(_ =>
            new LocalHttpServer.Response { Status = 302, Location = "file:///etc/passwd" });

        var result = await AllowLoopback().ResolveAsync(server.Url("/a.css"), ResourceType.StyleSheet);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_MoreRedirectsThanMaxRedirects_Refused()
    {
        using var server = new LocalHttpServer(path =>
            new LocalHttpServer.Response { Status = 302, Location = path + "x" });

        var result = await AllowLoopback(new HttpResourceOptions { MaxRedirects = 3 })
            .ResolveAsync(server.Url("/r"), ResourceType.StyleSheet);

        result.Should().BeNull();
        server.Requests.Count.Should().Be(4, "the original request plus three redirects, then stop");
    }

    [Fact]
    public async Task Resolve_RedirectToDomainOutsideAllowList_Refused()
    {
        LocalHttpServer? server = null;
        server = new LocalHttpServer(path => path == "/a.css"
            ? new LocalHttpServer.Response
            {
                Status = 302,
                Location = "http://localhost:" + server!.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/b.css",
            }
            : new LocalHttpServer.Response { Body = Payload });
        using var _ = server;

        var result = await AllowLoopback(new HttpResourceOptions { AllowedDomains = new[] { "127.0.0.1" } })
            .ResolveAsync(server.Url("/a.css"), ResourceType.StyleSheet);

        result.Should().BeNull();
        server.Requests.Should().Equal("/a.css");
    }

    [Fact]
    public async Task Resolve_RedirectHop_AddressCheckedAgain()
    {
        using var server = new LocalHttpServer(path => path == "/a.css"
            ? new LocalHttpServer.Response { Status = 302, Location = "/b.css" }
            : new LocalHttpServer.Response { Body = Payload });
        int checks = 0;
        var resolver = new HttpResourceResolver
        {
            // Allow the first connection only: the redirect's new connection must be checked and refused
            AddressFilter = _ => Interlocked.Increment(ref checks) == 1,
        };

        var result = await resolver.ResolveAsync(server.Url("/a.css"), ResourceType.StyleSheet);

        result.Should().BeNull();
        checks.Should().BeGreaterThan(1);
        server.Requests.Should().Equal("/a.css");
    }

    [Fact]
    public async Task Resolve_ContentLengthOverLimit_Refused()
    {
        using var server = Serve(new byte[1000]);

        var result = await AllowLoopback(new HttpResourceOptions { MaxResponseSizeBytes = 100 })
            .ResolveAsync(server.Url("/big.png"), ResourceType.Image);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_UnframedBodyOverLimit_Refused()
    {
        using var server = new LocalHttpServer(_ =>
            new LocalHttpServer.Response { Body = new byte[100_000], OmitContentLength = true });

        var result = await AllowLoopback(new HttpResourceOptions { MaxResponseSizeBytes = 1000 })
            .ResolveAsync(server.Url("/big.png"), ResourceType.Image);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_UnframedBodyWithinLimit_ReturnsBody()
    {
        using var server = new LocalHttpServer(_ =>
            new LocalHttpServer.Response { Body = Payload, OmitContentLength = true });

        var result = await AllowLoopback().ResolveAsync(server.Url("/a.css"), ResourceType.StyleSheet);

        result!.Data.Should().Equal(Payload);
    }

    [Fact]
    public async Task Resolve_ServerNeverAnswers_GivesUpAfterTimeout()
    {
        using var server = new LocalHttpServer(_ => new LocalHttpServer.Response { Hang = true });
        var sw = Stopwatch.StartNew();

        var result = await AllowLoopback(new HttpResourceOptions { TimeoutSeconds = 1 })
            .ResolveAsync(server.Url("/slow.css"), ResourceType.StyleSheet);

        result.Should().BeNull();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Resolve_CallerCancels_Throws()
    {
        using var server = new LocalHttpServer(_ => new LocalHttpServer.Response { Hang = true });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        Func<Task> act = () => AllowLoopback().ResolveAsync(server.Url("/slow.css"), ResourceType.StyleSheet, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Options_Defaults_AreSafe()
    {
        var options = new HttpResourceOptions();

        options.AllowPrivateNetworks.Should().BeFalse();
        options.MaxRedirects.Should().Be(5);
        options.TimeoutSeconds.Should().BeGreaterThan(0);
        options.MaxResponseSizeBytes.Should().BeGreaterThan(0);
        options.UserAgent.Should().StartWith("EggPdf/").And.NotBe("EggPdf/0.1");
    }

    [Fact]
    public async Task CompositeResolver_Default_UsesSafeHttp()
    {
        using var server = Serve();

        var result = await new CompositeResolver().ResolveAsync(server.Url("/a.css"), ResourceType.StyleSheet);

        result.Should().BeNull();
        server.Requests.Should().BeEmpty();
    }
}
