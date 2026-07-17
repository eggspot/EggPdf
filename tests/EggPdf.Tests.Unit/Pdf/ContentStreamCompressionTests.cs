using System;
using System.IO;
using System.IO.Compression;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>
/// Page content streams are FlateDecode-compressed by default in production
/// (PdfDocument.DefaultCompressContentStreams; the test assembly opts out
/// globally in TestSetup). These tests enable compression per document and
/// verify filter declaration, /Length accuracy, and zlib roundtrip.
/// </summary>
public class ContentStreamCompressionTests
{
    private const string Sentence = "Hello compression Hello compression Hello compression";

    private static byte[] Render(bool compress)
    {
        var doc = new PdfDocument { CompressContentStreams = compress };
        var page = doc.AddPage(595, 842);
        page.AddText(Sentence, 50, 700, "Helvetica", 12);
        return doc.ToByteArray();
    }

    private static string Latin1(byte[] pdf)
        => System.Text.Encoding.Latin1.GetString(pdf);

    /// <summary>Extract the first FlateDecode stream's bytes and declared /Length.</summary>
    private static byte[] ExtractFlateStream(byte[] pdf, out int declaredLength)
    {
        var text = Latin1(pdf); // Latin-1 maps chars to bytes 1:1
        int dictIdx = text.IndexOf("/Filter /FlateDecode", StringComparison.Ordinal);
        dictIdx.Should().BeGreaterThan(0, "a FlateDecode filter must be declared");

        int lenIdx = text.LastIndexOf("/Length ", dictIdx, StringComparison.Ordinal);
        int numStart = lenIdx + "/Length ".Length;
        int numEnd = numStart;
        while (numEnd < text.Length && char.IsDigit(text[numEnd])) numEnd++;
        declaredLength = int.Parse(text.Substring(numStart, numEnd - numStart));

        int streamIdx = text.IndexOf("stream\n", dictIdx, StringComparison.Ordinal)
            + "stream\n".Length;
        var bytes = new byte[declaredLength];
        Array.Copy(pdf, streamIdx, bytes, 0, declaredLength);
        return bytes;
    }

    private static byte[] InflateZlib(byte[] data)
    {
        // Skip the 2-byte zlib header; DeflateStream stops before the Adler-32.
        using var input = new MemoryStream(data, 2, data.Length - 2);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    [Fact]
    public void Disabled_ContentStreamIsRawText()
    {
        var pdf = Render(compress: false);
        var text = Latin1(pdf);

        text.Should().Contain("(" + Sentence + ") Tj",
            "uncompressed content streams keep readable operators");
        text.Should().NotContain("/FlateDecode",
            "a document with no embedded fonts or images has no compressed streams");
    }

    [Fact]
    public void Enabled_DeclaresFlateDecodeAndHidesRawOperators()
    {
        var pdf = Render(compress: true);
        var text = Latin1(pdf);

        text.Should().Contain("/Filter /FlateDecode");
        text.Should().NotContain("(" + Sentence + ")",
            "the operators must only exist inside the compressed stream");
    }

    [Fact]
    public void Enabled_StreamInflatesToTheSameOperators()
    {
        var pdf = Render(compress: true);
        var stream = ExtractFlateStream(pdf, out _);

        var ops = System.Text.Encoding.Latin1.GetString(InflateZlib(stream));
        ops.Should().Contain("(" + Sentence + ") Tj");
        ops.Should().Contain("BT");
        ops.Should().Contain("ET");
    }

    [Fact]
    public void Enabled_DeclaredLengthMatchesStreamBytes()
    {
        var pdf = Render(compress: true);
        var text = Latin1(pdf);

        ExtractFlateStream(pdf, out int declaredLength);
        int streamIdx = text.IndexOf("stream\n", text.IndexOf("/Filter /FlateDecode", StringComparison.Ordinal), StringComparison.Ordinal)
            + "stream\n".Length;
        text.Substring(streamIdx + declaredLength).Should().StartWith("\nendstream",
            "/Length must cover exactly the stream bytes");
    }

    [Fact]
    public void Enabled_ShrinksRepetitiveContent()
    {
        Render(compress: true).Length.Should().BeLessThan(Render(compress: false).Length,
            "compression must reduce output size for repetitive content");
    }
}
