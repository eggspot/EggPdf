using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using EggPdf.Text;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

[Collection(EggPdf.Tests.Unit.EndToEnd.ResourceLoadingCollection.Name)]
public class WebFontFormatTests
{
    private const int WoffHeaderSize = 44;
    private const int WoffTableEntrySize = 20;
    private const int TotalSfntSizeField = 16;

    private static byte[] Font() => SyntheticFonts.Build(Enumerable.Range(32, 95));

    private static string RenderWithFont(string fontSrc)
        => Encoding.GetEncoding("ISO-8859-1").GetString(HtmlToPdf.Render(
            "<html><head><style>@font-face { font-family: W; src: " + fontSrc + "; } p { font-family: W; }</style></head>" +
            "<body><p>Webfont</p></body></html>"));

    [Fact]
    public void WoffDecoder_RoundTrip_ParsesLikeTheOriginalFont()
    {
        var ttf = Font();

        var decoded = WoffDecoder.Decode(ToWoff(ttf));

        decoded.Should().NotBeNull();
        var original = TtfParser.Parse(ttf)!;
        var roundTripped = TtfParser.Parse(decoded!);
        roundTripped.Should().NotBeNull();
        roundTripped!.NumGlyphs.Should().Be(original.NumGlyphs);
        roundTripped.GetGlyphId('A').Should().Be(original.GetGlyphId('A'));
        roundTripped.GetGlyphId('A').Should().NotBe(0);
    }

    [Fact]
    public void WoffDecoder_TableThatInflatesShort_ReturnsNull()
    {
        var woff = ToWoff(Font());
        int firstEntry = WoffHeaderSize;
        uint compLength = ReadU32(woff, firstEntry + 8);
        WriteU32(woff, firstEntry + 12, compLength * 50);

        WoffDecoder.Decode(woff).Should().BeNull("a table that inflates to fewer bytes than its declared length is corrupt");
    }

    [Fact]
    public void WoffDecoder_CompressedLongerThanOriginal_ReturnsNull()
    {
        var woff = ToWoff(Font());
        int firstEntry = WoffHeaderSize;
        uint origLength = ReadU32(woff, firstEntry + 12);
        WriteU32(woff, firstEntry + 8, origLength + 1);

        WoffDecoder.Decode(woff).Should().BeNull();
    }

    [Fact]
    public void WoffDecoder_HeaderClaimingHugeSize_DoesNotAllocateIt()
    {
        var woff = ToWoff(Font());
        WriteU32(woff, TotalSfntSizeField, int.MaxValue);

        long before = GC.GetAllocatedBytesForCurrentThread();
        var decoded = WoffDecoder.Decode(woff);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        decoded.Should().NotBeNull("the table directory, not the header's total, decides the output size");
        allocated.Should().BeLessThan(4 * 1024 * 1024);
    }

    [Fact]
    public void FontFace_WoffSource_IsDecodedAndEmbedded()
    {
        const string url = "https://woff-source.example/w.woff";
        var woff = ToWoff(Font());
        FontUrlFetcher.FetchOverride = u => u == url ? woff : null;
        try
        {
            RenderWithFont($"url(\"{url}\") format(\"woff\")").Should().Contain("/FontFile2");
        }
        finally
        {
            FontUrlFetcher.FetchOverride = null;
        }
    }

    [Fact]
    public void FontFace_UnsupportedFirstSource_FallsBackToTheNext()
    {
        const string woff2Url = "https://fallback.example/w.woff2";
        const string woffUrl = "https://fallback.example/w.woff";
        var woff = ToWoff(Font());
        FontUrlFetcher.FetchOverride = u => u == woffUrl ? woff : u == woff2Url ? Encoding.ASCII.GetBytes("wOF2 not decodable") : null;
        try
        {
            RenderWithFont($"url(\"{woff2Url}\") format(\"woff2\"), url(\"{woffUrl}\") format(\"woff\")")
                .Should().Contain("/FontFile2");
        }
        finally
        {
            FontUrlFetcher.FetchOverride = null;
        }
    }

    [Fact]
    public void FontFace_DataUriSource_IsEmbedded()
    {
        var dataUri = "data:font/ttf;base64," + Convert.ToBase64String(Font());

        RenderWithFont($"url(\"{dataUri}\") format(\"truetype\")").Should().Contain("/FontFile2",
            "the comma inside a data: URI is not a separator between sources");
    }

    [Fact]
    public void FontFaceRule_DataUriSource_KeepsTheWholeUri()
    {
        var sheet = EggPdf.Css.Parser.CssStyleSheetParser.Parse(
            "@font-face { font-family: W; src: url(\"data:font/ttf;base64,AAAA\") format(\"truetype\"); }");

        var src = sheet.FontFaceRules.Single().Declarations.Single(d => d.Property == "src").Value;

        FontUrlFetcher.ParseFontSrcUrl(FontUrlFetcher.SplitFontSrcList(src)[0]).Should().Be("data:font/ttf;base64,AAAA");
    }

    [Fact]
    public void FontUrlFetcher_SourceList_SplitsOnlyBetweenSources()
    {
        var sources = FontUrlFetcher.SplitFontSrcList(
            "url(data:font/ttf;base64,AAAA) format('truetype'), local('A, B'), url(\"x,y.woff\")");

        sources.Should().Equal("url(data:font/ttf;base64,AAAA) format('truetype')", "local('A, B')", "url(\"x,y.woff\")");
    }

    private static byte[] ToWoff(byte[] ttf)
    {
        int numTables = (ttf[4] << 8) | ttf[5];
        var tables = new (uint Tag, uint Checksum, byte[] Original, byte[] Stored)[numTables];
        for (int i = 0; i < numTables; i++)
        {
            int record = 12 + i * 16;
            int offset = (int)ReadU32(ttf, record + 8);
            int length = (int)ReadU32(ttf, record + 12);
            var original = ttf.Skip(offset).Take(length).ToArray();
            var compressed = Zlib(original);
            tables[i] = (ReadU32(ttf, record), ReadU32(ttf, record + 4), original, compressed.Length < original.Length ? compressed : original);
        }

        using var woff = new MemoryStream();
        woff.Write(new byte[WoffHeaderSize + numTables * WoffTableEntrySize]);
        var header = new byte[WoffHeaderSize + numTables * WoffTableEntrySize];
        WriteU32(header, 0, 0x774F4646);
        WriteU32(header, 4, ReadU32(ttf, 0));
        header[12] = (byte)(numTables >> 8);
        header[13] = (byte)numTables;
        WriteU32(header, TotalSfntSizeField, (uint)ttf.Length);
        for (int i = 0; i < numTables; i++)
        {
            while (woff.Length % 4 != 0) woff.WriteByte(0);
            int entry = WoffHeaderSize + i * WoffTableEntrySize;
            WriteU32(header, entry, tables[i].Tag);
            WriteU32(header, entry + 4, (uint)woff.Length);
            WriteU32(header, entry + 8, (uint)tables[i].Stored.Length);
            WriteU32(header, entry + 12, (uint)tables[i].Original.Length);
            WriteU32(header, entry + 16, tables[i].Checksum);
            woff.Write(tables[i].Stored);
        }
        var bytes = woff.ToArray();
        WriteU32(header, 8, (uint)bytes.Length);
        header.CopyTo(bytes, 0);
        return bytes;
    }

    private static byte[] Zlib(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data, 0, data.Length);
        return output.ToArray();
    }

    private static uint ReadU32(byte[] data, int offset)
        => (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }
}
