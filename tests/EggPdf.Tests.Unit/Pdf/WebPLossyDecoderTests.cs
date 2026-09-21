using System;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>
/// Lossy WebP (VP8) decoding, checked pixel by pixel against what Chrome decoded the same files to.
/// The fixtures carry a VP8X header with an ICC profile, so they also cover extended-container parsing;
/// the alpha ones cover the ALPH chunk (lossless-compressed, filtered).
/// </summary>
public class WebPLossyDecoderTests
{
    private static (int maxDiff, double meanDiff, int badPixels) Compare(byte[] mine, byte[] chrome, bool skipTransparent)
    {
        int max = 0, bad = 0; long sum = 0; int counted = 0;
        for (int i = 0; i < chrome.Length; i += 4)
        {
            // Chrome's getImageData un-premultiplies, which is lossy for translucent pixels: compare
            // colour only where alpha is opaque, and alpha always.
            int alphaDiff = Math.Abs(mine[i + 3] - chrome[i + 3]);
            max = Math.Max(max, alphaDiff);
            if (alphaDiff > 0) bad++;
            if (skipTransparent && chrome[i + 3] != 255) continue;

            for (int c = 0; c < 3; c++)
            {
                int d = Math.Abs(mine[i + c] - chrome[i + c]);
                max = Math.Max(max, d); sum += d; counted++;
                if (d > 2) bad++;
            }
        }
        return (max, counted == 0 ? 0 : (double)sum / counted, bad);
    }

    public static System.Collections.Generic.IEnumerable<object[]> Fixtures()
    {
        foreach (var f in WebPLossyFixtures.All) yield return new object[] { f.Name };
    }

    private static WebPLossyFixtures.Fixture Get(string name)
    {
        foreach (var f in WebPLossyFixtures.All) if (f.Name == name) return f;
        throw new ArgumentException(name);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Decode_MatchesChromesDecodedPixels(string name)
    {
        var fixture = Get(name);
        var decoded = WebPDecoder.Decode(Convert.FromBase64String(fixture.WebPBase64));

        decoded.Should().NotBeNull("the VP8 stream must decode");
        var (w, h, rgba) = decoded!.Value;
        w.Should().Be(fixture.Width);
        h.Should().Be(fixture.Height);

        var chrome = Convert.FromBase64String(fixture.RgbaBase64);
        var (maxDiff, meanDiff, bad) = Compare(rgba, chrome, skipTransparent: name.StartsWith("alpha", StringComparison.Ordinal));

        bad.Should().Be(0, $"every channel must be within 2 of Chrome's decode (max diff {maxDiff}, mean {meanDiff:F3})");
        meanDiff.Should().BeLessThan(0.6);
    }

    [Fact]
    public void Decode_LossyWithAlpha_ProducesTranslucentPixels()
    {
        var decoded = WebPDecoder.Decode(Convert.FromBase64String(WebPLossyFixtures.Alpha40x30.WebPBase64));
        var rgba = decoded!.Value.rgba;

        bool anyTransparent = false, anyOpaque = false, anyPartial = false;
        for (int i = 3; i < rgba.Length; i += 4)
        {
            anyTransparent |= rgba[i] == 0;
            anyOpaque |= rgba[i] == 255;
            anyPartial |= rgba[i] > 0 && rgba[i] < 255;
        }
        anyTransparent.Should().BeTrue("the canvas background was transparent");
        anyOpaque.Should().BeTrue();
        anyPartial.Should().BeTrue("the translucent circle and anti-aliased edges");
    }

    [Fact]
    public void FromWebP_OpaqueLossyImage_HasNoSoftMask()
    {
        var image = PdfImage.FromWebP("w", Convert.FromBase64String(WebPLossyFixtures.Lossy48x32.WebPBase64));

        image.Should().NotBeNull();
        image!.Width.Should().Be(48);
        image.Height.Should().Be(32);
        image.SMaskData.Should().BeNull("a fully opaque image needs no alpha plane");
        image.Data.Length.Should().Be(48 * 32 * 3);
    }

    [Fact]
    public void FromWebP_LossyWithAlpha_CarriesASoftMask()
    {
        var image = PdfImage.FromWebP("w", Convert.FromBase64String(WebPLossyFixtures.Alpha40x30.WebPBase64));

        image.Should().NotBeNull();
        image!.SMaskData.Should().NotBeNull();
        image.SMaskData!.Length.Should().Be(40 * 30);
    }

    [Fact]
    public void Decode_TruncatedLossyData_DoesNotThrow()
    {
        var bytes = Convert.FromBase64String(WebPLossyFixtures.Lossy100x70.WebPBase64);
        foreach (int keep in new[] { 20, 60, bytes.Length / 2, bytes.Length - 40 })
        {
            var cut = new byte[keep];
            Array.Copy(bytes, cut, keep);
            var act = () => WebPDecoder.Decode(cut);
            act.Should().NotThrow();

            // Whatever survives the cut is either rejected or a picture of the right size, never garbage dimensions
            var decoded = act();
            if (decoded.HasValue)
            {
                decoded.Value.width.Should().Be(100);
                decoded.Value.height.Should().Be(70);
                decoded.Value.rgba.Length.Should().Be(100 * 70 * 4);
            }
        }
    }

    [Fact]
    public void Decode_GarbageAndNonKeyFrames_ReturnNull()
    {
        WebPDecoder.Decode(new byte[] { 1, 2, 3 }).Should().BeNull();
        WebPDecoder.Decode(System.Text.Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 \0\0\0\0")).Should().BeNull();

        // A valid stream with the key-frame bit flipped is an inter frame: unsupported, not garbage output
        var bytes = Convert.FromBase64String(WebPLossyFixtures.Lossy48x32.WebPBase64);
        int vp8 = Array.IndexOf(bytes, (byte)'V', 12 + 10);
        while (vp8 >= 0 && System.Text.Encoding.ASCII.GetString(bytes, vp8, 4) != "VP8 ") vp8 = Array.IndexOf(bytes, (byte)'V', vp8 + 1);
        vp8.Should().BeGreaterThan(0);
        bytes[vp8 + 8] |= 1;
        WebPDecoder.Decode(bytes).Should().BeNull();
    }

    [Fact]
    public void Decode_ExtendedContainer_IgnoresIccpChunk()
    {
        // The fixtures are VP8X + ICCP + VP8: reaching the pixels proves unknown chunks are skipped
        var bytes = Convert.FromBase64String(WebPLossyFixtures.Lossy33x21.WebPBase64);
        System.Text.Encoding.ASCII.GetString(bytes, 12, 4).Should().Be("VP8X");
        WebPDecoder.Decode(bytes).Should().NotBeNull();
    }

    [Fact]
    public async System.Threading.Tasks.Task Html_ImgWithLossyWebP_EmbedsTheImage()
    {
        var b64 = WebPLossyFixtures.Lossy100x70.WebPBase64;
        var html = $"<img src='data:image/webp;base64,{b64}' width='100' height='70'>";

        var pdf = System.Text.Encoding.Latin1.GetString(await EggPdf.HtmlToPdf.RenderAsync(html));

        pdf.Should().Contain("/Subtype /Image");
        pdf.Should().Contain("/Width 100");
        pdf.Should().Contain("/Height 70");
    }
}
