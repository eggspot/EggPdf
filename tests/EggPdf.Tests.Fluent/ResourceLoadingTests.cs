using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EggPdf.Core;
using EggPdf.Core.Resources;
using EggPdf.Fluent;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Fluent;

/// <summary>
/// <see cref="DocumentDescriptor.ResourceResolver"/> and <see cref="DocumentDescriptor.AllowLocalFileAccess"/>:
/// the fluent surface of <see cref="PdfRenderOptions.ResourceResolver"/> / <see cref="PdfRenderOptions.AllowLocalFileAccess"/>.
/// </summary>
public class ResourceLoadingTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==");

    private sealed class PngResolver : IResourceResolver
    {
        public ConcurrentQueue<string> Calls { get; } = new();

        public Task<ResourceResult?> ResolveAsync(string url, ResourceType type, CancellationToken ct = default)
        {
            Calls.Enqueue(url);
            return Task.FromResult<ResourceResult?>(type == ResourceType.Image ? new ResourceResult(Png) : null);
        }
    }

    private static string Latin1(byte[] pdf) => Encoding.GetEncoding("ISO-8859-1").GetString(pdf);

    [Fact]
    public void ResourceResolver_LoadsImagesThroughIt()
    {
        var resolver = new PngResolver();
        var doc = Document.New().ResourceResolver(resolver);
        doc.AddPage().Content().Item().Image("https://cdn.example/logo.png", 20, 20);

        var pdf = doc.Render();

        Latin1(pdf).Should().Contain("/Subtype /Image");
        resolver.Calls.Should().Contain("https://cdn.example/logo.png");
    }

    [Fact]
    public async Task ResourceResolver_AlsoUsedByRenderAsync()
    {
        var resolver = new PngResolver();
        var doc = Document.New().ResourceResolver(resolver);
        doc.AddPage().Content().Item().Image("https://cdn.example/logo.png", 20, 20);

        var pdf = await doc.RenderAsync();

        Latin1(pdf).Should().Contain("/Subtype /Image");
    }

    [Fact]
    public void AllowLocalFileAccessFalse_DoesNotReadDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), "eggpdf_fluent_" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, Png);
        try
        {
            var warnings = new WarningCollector();
            var doc = Document.New().AllowLocalFileAccess(false).CollectWarnings(warnings);
            doc.AddPage().Content().Item().Image(path, 20, 20);

            var pdf = doc.Render();

            Latin1(pdf).Should().NotContain("/Subtype /Image");
            warnings.Warnings.Should().Contain(w => w.Code == WarningCodes.ResourceBlocked);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResourceResolver_Null_Throws()
    {
        Action act = () => Document.New().ResourceResolver(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
