using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class FlateStreamTests
{
    private const string VietnameseForcingCidFontEmbedding = "Hiệp hội Việt Nam";

    private static List<byte[]> ExtractFlateStreams(byte[] pdf)
    {
        var result = new List<byte[]>();
        var text = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);

        foreach (Match m in Regex.Matches(text, @"/Filter /FlateDecode[^>]*>>\s*stream\r?\n"))
        {
            int start = m.Index + m.Length;
            int end = text.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0) continue;
            int len = end - start;
            while (len > 0 && (pdf[start + len - 1] == (byte)'\n' || pdf[start + len - 1] == (byte)'\r'))
                len--;
            var bytes = new byte[len];
            Array.Copy(pdf, start, bytes, 0, len);
            result.Add(bytes);
        }
        return result;
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (byte t in data)
        {
            a = (a + t) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    [Fact]
    public async Task EmbeddedFontStreams_AreValidZlib()
    {
        var pdf = await HtmlToPdf.RenderAsync("<html><body><p>" + VietnameseForcingCidFontEmbedding + "</p></body></html>");

        var streams = ExtractFlateStreams(pdf);
        streams.Should().NotBeEmpty("an embedded font produces at least FontFile2 and ToUnicode streams");

        foreach (var s in streams)
        {
            s.Length.Should().BeGreaterThan(6);
            (s[0] & 0x0F).Should().Be(8,
                "a /FlateDecode stream is zlib (RFC 1950), not raw deflate, which strict viewers reject: CMF's low nibble is 8");
            (((s[0] << 8) | s[1]) % 31).Should().Be(0, "zlib header must satisfy the FCHECK divisibility rule");

            byte[] inflated;
            using (var ms = new MemoryStream(s, 2, s.Length - 6))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                ds.CopyTo(outMs);
                inflated = outMs.ToArray();
            }
            inflated.Length.Should().BeGreaterThan(0);

            uint expected = Adler32(inflated);
            uint actual = (uint)((s[s.Length - 4] << 24) | (s[s.Length - 3] << 16) |
                                 (s[s.Length - 2] << 8) | s[s.Length - 1]);
            actual.Should().Be(expected, "the trailing 4 bytes must be the Adler-32 of the inflated data");
        }
    }

    [Fact]
    public async Task UppercaseTransformedVietnamese_HasNoNotdefGlyphs()
    {
        var pdf = await HtmlToPdf.RenderAsync(
            "<html><body><p style=\"text-transform:uppercase\">" + VietnameseForcingCidFontEmbedding + "</p></body></html>");

        var text = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);
        var cidRuns = Regex.Matches(text, @"<([0-9A-Fa-f]+)>\s*Tj");
        cidRuns.Should().NotBeEmpty("the Vietnamese text is painted with an embedded CID font");
        foreach (Match m in cidRuns)
        {
            var hex = m.Groups[1].Value;
            for (int i = 0; i + 4 <= hex.Length; i += 4)
                hex.Substring(i, 4).Should().NotBe("0000",
                    "text-transform is applied at paint time, so the subset's code points must be uppercased too, " +
                    "or the uppercase glyphs are missing and paint as CID 0 (.notdef)");
        }
    }
}
