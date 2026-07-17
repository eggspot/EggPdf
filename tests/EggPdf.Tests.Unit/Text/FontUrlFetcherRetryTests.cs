using System;
using EggPdf.Text;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

/// <summary>
/// A transient network failure must not poison a font URL for the process
/// lifetime — failures may only suppress retries for a bounded window,
/// while successful fetches stay cached forever (font URLs are immutable).
/// </summary>
public class FontUrlFetcherRetryTests
{
    private static readonly byte[] FakeFont = { 0x00, 0x01, 0x00, 0x00 };

    [Fact]
    public void Fetch_FailureThenSuccess_RetriesInsteadOfPoisoningCache()
    {
        var url = "https://retry-test.example/failure-then-success.ttf";
        int calls = 0;
        var oldInterval = FontUrlFetcher.FailureRetryInterval;
        FontUrlFetcher.FetchOverride = _ => ++calls == 1 ? null : FakeFont;
        FontUrlFetcher.FailureRetryInterval = TimeSpan.Zero;
        try
        {
            FontUrlFetcher.Fetch(url).Should().BeNull("the first fetch fails");
            FontUrlFetcher.Fetch(url).Should().Equal(FakeFont,
                "a failed fetch must be retried once the retry window elapses");
        }
        finally
        {
            FontUrlFetcher.FetchOverride = null;
            FontUrlFetcher.FailureRetryInterval = oldInterval;
        }
    }

    [Fact]
    public void Fetch_Success_IsCachedAndNotRefetched()
    {
        var url = "https://retry-test.example/success-cached.ttf";
        int calls = 0;
        FontUrlFetcher.FetchOverride = _ => { calls++; return FakeFont; };
        try
        {
            FontUrlFetcher.Fetch(url).Should().Equal(FakeFont);
            FontUrlFetcher.Fetch(url).Should().Equal(FakeFont);
            calls.Should().Be(1, "a successful fetch is cached for the process lifetime");
        }
        finally
        {
            FontUrlFetcher.FetchOverride = null;
        }
    }

    [Fact]
    public void Fetch_FailureWithinRetryWindow_DoesNotHammerTheUrl()
    {
        var url = "https://retry-test.example/failure-suppressed.ttf";
        int calls = 0;
        var oldInterval = FontUrlFetcher.FailureRetryInterval;
        FontUrlFetcher.FetchOverride = _ => { calls++; return null; };
        FontUrlFetcher.FailureRetryInterval = TimeSpan.FromHours(1);
        try
        {
            FontUrlFetcher.Fetch(url).Should().BeNull();
            FontUrlFetcher.Fetch(url).Should().BeNull();
            calls.Should().Be(1,
                "within the retry window a known-bad URL must not add a network timeout to every render");
        }
        finally
        {
            FontUrlFetcher.FetchOverride = null;
            FontUrlFetcher.FailureRetryInterval = oldInterval;
        }
    }

    [Fact]
    public void Fetch_EmptyPayload_IsTreatedAsFailureAndRetried()
    {
        var url = "https://retry-test.example/empty-then-success.ttf";
        int calls = 0;
        var oldInterval = FontUrlFetcher.FailureRetryInterval;
        FontUrlFetcher.FetchOverride = _ => ++calls == 1 ? new byte[0] : FakeFont;
        FontUrlFetcher.FailureRetryInterval = TimeSpan.Zero;
        try
        {
            FontUrlFetcher.Fetch(url).Should().BeNull("an empty font file is not usable");
            FontUrlFetcher.Fetch(url).Should().Equal(FakeFont);
        }
        finally
        {
            FontUrlFetcher.FetchOverride = null;
            FontUrlFetcher.FailureRetryInterval = oldInterval;
        }
    }
}
