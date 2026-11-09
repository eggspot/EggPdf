using System;
using System.Threading.Tasks;
using EggPdf.Core.Resources;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Core;

public class DataUriResolverTests
{
    private const byte PngSignatureFirstByte = 0x89;

    private readonly DataUriResolver _resolver = new();

    [Fact]
    public async Task Resolve_Base64Png_DecodesCorrectly()
    {
        var onePixelRedPng = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==";

        var result = await _resolver.ResolveAsync(onePixelRedPng, ResourceType.Image);

        result.Should().NotBeNull();
        result!.MimeType.Should().Be("image/png");
        result.Data.Should().NotBeEmpty();
        result.Data[0].Should().Be(PngSignatureFirstByte);
        result.Data[1].Should().Be((byte)'P');
    }

    [Fact]
    public async Task Resolve_Base64Jpeg_DecodesCorrectly()
    {
        var jpegSignature = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var url = $"data:image/jpeg;base64,{Convert.ToBase64String(jpegSignature)}";

        var result = await _resolver.ResolveAsync(url, ResourceType.Image);

        result.Should().NotBeNull();
        result!.MimeType.Should().Be("image/jpeg");
        result.Data.Should().BeEquivalentTo(jpegSignature);
    }

    [Fact]
    public async Task Resolve_PlainText_DecodesCorrectly()
    {
        var url = "data:text/css,body%20%7B%20color%3A%20red%20%7D";

        var result = await _resolver.ResolveAsync(url, ResourceType.StyleSheet);

        result.Should().NotBeNull();
        result!.MimeType.Should().Be("text/css");
        var text = System.Text.Encoding.UTF8.GetString(result.Data);
        text.Should().Be("body { color: red }");
    }

    [Fact]
    public async Task Resolve_NonDataUri_ReturnsNull()
    {
        var result = await _resolver.ResolveAsync("https://example.com/image.png", ResourceType.Image);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_EmptyData_ReturnsEmptyBytes()
    {
        var url = "data:text/plain;base64,";

        var result = await _resolver.ResolveAsync(url, ResourceType.Other);

        result.Should().NotBeNull();
        result!.Data.Should().BeEmpty();
    }
}
