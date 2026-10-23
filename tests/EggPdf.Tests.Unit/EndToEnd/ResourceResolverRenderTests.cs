using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EggPdf.Core;
using EggPdf.Core.Resources;
using EggPdf.Html;
using EggPdf.Text;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// <see cref="PdfRenderOptions.ResourceResolver"/> and <see cref="PdfRenderOptions.AllowLocalFileAccess"/>:
/// every external resource (stylesheets, @import, @font-face, images, backgrounds) goes through the caller's
/// resolver, nothing bypasses it through a process-wide cache, and local file access can be switched off.
/// </summary>
[Collection(ResourceLoadingCollection.Name)]
public class ResourceResolverRenderTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==");

    private static readonly byte[] BoldCss = Encoding.UTF8.GetBytes(".b { font-weight: bold; }");

    /// <summary>Serves fixed bytes per URL and records every call.</summary>
    private sealed class RecordingResolver : IResourceResolver
    {
        private readonly Dictionary<string, byte[]> _responses;
        public ConcurrentQueue<(string Url, ResourceType Type)> Calls { get; } = new();
        public Func<string, Exception?>? Throw { get; set; }

        public RecordingResolver(Dictionary<string, byte[]>? responses = null)
        {
            _responses = responses ?? new Dictionary<string, byte[]>();
        }

        public Task<ResourceResult?> ResolveAsync(string url, ResourceType type, CancellationToken ct = default)
        {
            Calls.Enqueue((url, type));
            var ex = Throw?.Invoke(url);
            if (ex != null) throw ex;
            return Task.FromResult(_responses.TryGetValue(url, out var data) ? new ResourceResult(data, null, url) : null);
        }
    }

    private static string Latin1(byte[] pdf) => Encoding.GetEncoding("ISO-8859-1").GetString(pdf);

    private static PdfRenderOptions With(IResourceResolver resolver, WarningCollector? warnings = null) =>
        new() { ResourceResolver = resolver, Warnings = warnings };

    [Fact]
    public void Options_Defaults_KeepLegacyBehaviour()
    {
        var options = new PdfRenderOptions();

        options.ResourceResolver.Should().BeNull();
        options.AllowLocalFileAccess.Should().BeTrue();
    }

    [Fact]
    public void LinkStylesheet_LoadedThroughResolver()
    {
        var resolver = new RecordingResolver(new() { ["https://cdn.example/theme.css"] = BoldCss });

        var pdf = HtmlToPdf.Render(
            "<html><head><link rel=\"stylesheet\" href=\"https://cdn.example/theme.css\"></head>" +
            "<body><p class=\"b\">Styled</p></body></html>", With(resolver));

        Latin1(pdf).Should().Contain("Helvetica-Bold");
        resolver.Calls.Should().Contain(("https://cdn.example/theme.css", ResourceType.StyleSheet));
    }

    [Fact]
    public void Import_LoadedThroughResolver()
    {
        var resolver = new RecordingResolver(new() { ["https://cdn.example/base.css"] = BoldCss });

        var pdf = HtmlToPdf.Render(
            "<html><head><style>@import url(\"https://cdn.example/base.css\");</style></head>" +
            "<body><p class=\"b\">Imported</p></body></html>", With(resolver));

        Latin1(pdf).Should().Contain("Helvetica-Bold");
        resolver.Calls.Should().Contain(("https://cdn.example/base.css", ResourceType.StyleSheet));
    }

    [Fact]
    public void Img_RemoteUrl_LoadedThroughResolver()
    {
        var resolver = new RecordingResolver(new() { ["https://cdn.example/logo.png"] = Png });

        var pdf = HtmlToPdf.Render(
            "<html><body><img src=\"https://cdn.example/logo.png\" width=\"20\" height=\"20\"></body></html>",
            With(resolver));

        Latin1(pdf).Should().Contain("/Subtype /Image");
        resolver.Calls.Should().Contain(("https://cdn.example/logo.png", ResourceType.Image));
    }

    [Fact]
    public void BackgroundImage_LoadedThroughResolver()
    {
        var resolver = new RecordingResolver(new() { ["https://cdn.example/bg.png"] = Png });

        var pdf = HtmlToPdf.Render(
            "<html><body><div style=\"width:50px;height:50px;background-image:url('https://cdn.example/bg.png')\"></div></body></html>",
            With(resolver));

        Latin1(pdf).Should().Contain("/Subtype /Image");
        resolver.Calls.Should().Contain(("https://cdn.example/bg.png", ResourceType.Image));
    }

    [Fact]
    public void FontFace_LoadedThroughResolver()
    {
        var fontPath = TestFonts.FindAny();
        var responses = new Dictionary<string, byte[]>();
        if (fontPath != null) responses["https://fonts.example/w.ttf"] = File.ReadAllBytes(fontPath);
        var resolver = new RecordingResolver(responses);

        var pdf = HtmlToPdf.Render(
            "<html><head><style>@font-face { font-family: W; src: url(\"https://fonts.example/w.ttf\"); }" +
            "p { font-family: W; }</style></head><body><p>Webfont</p></body></html>", With(resolver));

        resolver.Calls.Should().Contain(("https://fonts.example/w.ttf", ResourceType.Font));
        if (fontPath != null)
            Latin1(pdf).Should().Contain("/FontFile2", "the resolver's font bytes are embedded");
    }

    [Fact]
    public void DataUri_DecodedInProcess_NeverSentToResolver()
    {
        var resolver = new RecordingResolver();
        var css = "data:text/css;base64," + Convert.ToBase64String(BoldCss);

        var pdf = HtmlToPdf.Render(
            $"<html><head><link rel=\"stylesheet\" href=\"{css}\"></head><body><p class=\"b\">x</p>" +
            $"<img src=\"data:image/png;base64,{Convert.ToBase64String(Png)}\"></body></html>", With(resolver));

        Latin1(pdf).Should().Contain("Helvetica-Bold").And.Contain("/Subtype /Image");
        resolver.Calls.Should().BeEmpty();
    }

    [Fact]
    public void SameUrlTwice_ResolvedOncePerRender()
    {
        var resolver = new RecordingResolver(new() { ["https://cdn.example/logo.png"] = Png });

        HtmlToPdf.Render(
            "<html><body><img src=\"https://cdn.example/logo.png\"><img src=\"https://cdn.example/logo.png\"></body></html>",
            With(resolver));

        resolver.Calls.Count(c => c.Url == "https://cdn.example/logo.png").Should().Be(1);
    }

    [Fact]
    public void ResolverThrows_RenderSucceedsWithWarning()
    {
        var resolver = new RecordingResolver { Throw = _ => new InvalidOperationException("boom") };
        var warnings = new WarningCollector();

        var pdf = HtmlToPdf.Render(
            "<html><body><p>Still renders</p><img src=\"https://cdn.example/logo.png\"></body></html>",
            With(resolver, warnings));

        PdfAssert.ValidPdf(pdf, "Still renders");
        warnings.Warnings.Should().Contain(w => w.Code == WarningCodes.ImageLoadFailed && w.Message.Contains("boom"));
    }

    [Fact]
    public void ResolverIgnoresCancellation_GivesUpAfterResourceTimeout()
    {
        var old = HtmlToPdf.ResourceTimeout;
        HtmlToPdf.ResourceTimeout = TimeSpan.FromMilliseconds(300);
        try
        {
            var resolver = new HangingResolver();
            var warnings = new WarningCollector();

            var pdf = HtmlToPdf.Render("<html><body><p>Done</p><img src=\"https://slow.example/a.png\"></body></html>",
                With(resolver, warnings));

            PdfAssert.ValidPdf(pdf, "Done");
            warnings.Warnings.Should().Contain(w => w.Code == WarningCodes.ResourceTimeout);
        }
        finally
        {
            HtmlToPdf.ResourceTimeout = old;
        }
    }

    [Fact]
    public async Task RenderAsync_CallerCancels_CancellationReachesResolver()
    {
        var resolver = new CancellableResolver();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        Func<Task> act = () => HtmlToPdf.RenderAsync(
            "<html><body><img src=\"https://slow.example/a.png\"></body></html>", With(resolver), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        // The render stops waiting as soon as the token fires; the resolver observes it on its own thread
        SpinWait.SpinUntil(() => resolver.SawCancellation, TimeSpan.FromSeconds(5)).Should().BeTrue();
    }

    [Fact]
    public void AllowLocalFileAccessFalse_RefusesPathsBeforeTheResolver()
    {
        var resolver = new RecordingResolver(new() { ["logo.png"] = Png, ["file:///etc/logo.png"] = Png });
        var warnings = new WarningCollector();
        var options = new PdfRenderOptions { ResourceResolver = resolver, AllowLocalFileAccess = false, Warnings = warnings };

        var pdf = HtmlToPdf.Render(
            "<html><body><img src=\"logo.png\"><img src=\"file:///etc/logo.png\"><img src=\"/etc/logo.png\"></body></html>",
            options);

        Latin1(pdf).Should().NotContain("/Subtype /Image");
        resolver.Calls.Should().BeEmpty();
        warnings.Warnings.Count(w => w.Code == WarningCodes.ResourceBlocked).Should().Be(3);
    }

    [Fact]
    public void AllowLocalFileAccessFalse_RemoteAndDataUrlsStillLoad()
    {
        var resolver = new RecordingResolver(new() { ["https://cdn.example/theme.css"] = BoldCss });
        var options = new PdfRenderOptions { ResourceResolver = resolver, AllowLocalFileAccess = false };

        var pdf = HtmlToPdf.Render(
            "<html><head><link rel=\"stylesheet\" href=\"https://cdn.example/theme.css\"></head><body><p class=\"b\">x</p>" +
            $"<img src=\"data:image/png;base64,{Convert.ToBase64String(Png)}\"></body></html>", options);

        Latin1(pdf).Should().Contain("Helvetica-Bold").And.Contain("/Subtype /Image");
    }

    [Fact]
    public void AllowLocalFileAccessFalse_WithoutResolver_DoesNotReadDisk()
    {
        using var dir = new TempDir();
        var cssPath = dir.Write("theme.css", BoldCss);
        var pngPath = dir.Write("logo.png", Png);
        var warnings = new WarningCollector();

        var pdf = HtmlToPdf.Render(
            $"<html><head><link rel=\"stylesheet\" href=\"{cssPath}\"></head>" +
            $"<body><p class=\"b\">x</p><img src=\"{pngPath}\"></body></html>",
            new PdfRenderOptions { AllowLocalFileAccess = false, Warnings = warnings });

        var text = Latin1(pdf);
        text.Should().NotContain("Helvetica-Bold");
        text.Should().NotContain("/Subtype /Image");
        warnings.Warnings.Count(w => w.Code == WarningCodes.ResourceBlocked).Should().Be(2);
    }

    [Fact]
    public void NoResolver_LocalFilesStillLoadByDefault()
    {
        using var dir = new TempDir();
        var cssPath = dir.Write("theme.css", BoldCss);
        var pngPath = dir.Write("logo.png", Png);

        var pdf = HtmlToPdf.Render(
            $"<html><head><link rel=\"stylesheet\" href=\"{cssPath}\"></head>" +
            $"<body><p class=\"b\">x</p><img src=\"{pngPath}\"></body></html>", new PdfRenderOptions());

        Latin1(pdf).Should().Contain("Helvetica-Bold").And.Contain("/Subtype /Image");
    }

    [Fact]
    public void NoResolver_RelativeImageResolvedAgainstBasePath()
    {
        using var dir = new TempDir();
        dir.Write("logo.png", Png);

        var pdf = HtmlToPdf.Render("<html><body><img src=\"logo.png\"></body></html>", dir.Path);

        Latin1(pdf).Should().Contain("/Subtype /Image");
    }

    [Fact]
    public void NoResolver_FontFaceFilePath_LoadedAgainstBasePath()
    {
        var fontPath = TestFonts.FindAny();
        if (fontPath == null) return;
        using var dir = new TempDir();
        dir.Write("w.ttf", File.ReadAllBytes(fontPath));

        var pdf = HtmlToPdf.Render(
            "<html><head><style>@font-face { font-family: W; src: url(\"w.ttf\"); }" +
            "p { font-family: W; }</style></head><body><p>Webfont</p></body></html>", dir.Path);

        Latin1(pdf).Should().Contain("/FontFile2", "a file-path @font-face source is read like any other local resource");
    }

    [Fact]
    public void ResolverRender_NeverServedFromLegacyProcessCache()
    {
        const string url = "https://cache-isolation.example/theme.css";
        FontUrlFetcher.FetchOverride = u => u == url ? BoldCss : null;
        try
        {
            // A trusted render without a resolver fetches the URL and caches it process-wide
            var legacy = HtmlToPdf.Render(
                $"<html><head><link rel=\"stylesheet\" href=\"{url}\"></head><body><p class=\"b\">x</p></body></html>");
            Latin1(legacy).Should().Contain("Helvetica-Bold");

            // A render with a resolver that refuses the URL must not get the cached copy
            var resolver = new RecordingResolver();
            var restricted = HtmlToPdf.Render(
                $"<html><head><link rel=\"stylesheet\" href=\"{url}\"></head><body><p class=\"b\">x</p></body></html>",
                With(resolver));

            Latin1(restricted).Should().NotContain("Helvetica-Bold");
            resolver.Calls.Should().Contain((url, ResourceType.StyleSheet));
        }
        finally
        {
            FontUrlFetcher.FetchOverride = null;
        }
    }

    [Fact]
    public void ResolverRender_WebFontNotServedFromLegacyParseCache()
    {
        var fontPath = TestFonts.FindAny();
        if (fontPath == null) return;
        const string url = "https://cache-isolation.example/w.ttf";
        var fontBytes = File.ReadAllBytes(fontPath);
        const string html = "<html><head><style>@font-face { font-family: W; src: url(\"" + url + "\"); }" +
                            "p { font-family: W; }</style></head><body><p>Webfont</p></body></html>";
        FontUrlFetcher.FetchOverride = u => u == url ? fontBytes : null;
        try
        {
            Latin1(HtmlToPdf.Render(html)).Should().Contain("/FontFile2");

            var resolver = new RecordingResolver();
            var restricted = HtmlToPdf.Render(html, With(resolver));

            Latin1(restricted).Should().NotContain("/FontFile2");
            resolver.Calls.Should().Contain((url, ResourceType.Font));
        }
        finally
        {
            FontUrlFetcher.FetchOverride = null;
        }
    }

    [Fact]
    public void RenderDocument_WithOptions_UsesResolver()
    {
        var resolver = new RecordingResolver(new() { ["https://cdn.example/logo.png"] = Png });
        var document = HtmlParser.Parse("<html><body><img src=\"https://cdn.example/logo.png\"></body></html>");

        var pdf = HtmlToPdf.RenderDocument(document, null, With(resolver));

        Latin1(pdf).Should().Contain("/Subtype /Image");
    }

    [Fact]
    public void RenderDocument_WithHtmlOnlyOption_Throws()
    {
        var document = HtmlParser.Parse("<html><body><p>x</p></body></html>");

        Action act = () => HtmlToPdf.RenderDocument(document, null, new PdfRenderOptions { PageSize = "A5" });

        act.Should().Throw<ArgumentException>().WithMessage("*PageSize*");
    }

    private sealed class HangingResolver : IResourceResolver
    {
        public Task<ResourceResult?> ResolveAsync(string url, ResourceType type, CancellationToken ct = default) =>
            new TaskCompletionSource<ResourceResult?>().Task; // never completes, ignores ct
    }

    private sealed class CancellableResolver : IResourceResolver
    {
        public volatile bool SawCancellation;

        public async Task<ResourceResult?> ResolveAsync(string url, ResourceType type, CancellationToken ct = default)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
                return null;
            }
            catch (OperationCanceledException)
            {
                SawCancellation = true;
                throw;
            }
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "eggpdf_res_" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public string Write(string name, byte[] data)
        {
            var full = System.IO.Path.Combine(Path, name);
            File.WriteAllBytes(full, data);
            return full;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch { }
        }
    }
}

/// <summary>Tests that swap process-wide resource-loading seams (FontUrlFetcher.FetchOverride, HtmlToPdf.ResourceTimeout).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class ResourceLoadingCollection
{
    public const string Name = "Resource loading seams";
}

/// <summary>Locates an installed TrueType font for tests that need real font bytes.</summary>
internal static class TestFonts
{
    public static string? FindAny()
    {
        string[] dirs = OperatingSystem.IsWindows() ? new[] { @"C:\Windows\Fonts" }
            : OperatingSystem.IsMacOS() ? new[] { "/System/Library/Fonts", "/Library/Fonts" }
            : new[] { "/usr/share/fonts", "/usr/local/share/fonts" };
        foreach (var name in new[] { "arial", "DejaVuSans", "Liberation" })
        {
            foreach (var dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    var match = Directory.GetFiles(dir, "*.ttf", SearchOption.AllDirectories)
                        .FirstOrDefault(f => System.IO.Path.GetFileNameWithoutExtension(f)
                            .Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return match;
                }
                catch { /* unreadable font dir */ }
            }
        }
        return null;
    }
}
