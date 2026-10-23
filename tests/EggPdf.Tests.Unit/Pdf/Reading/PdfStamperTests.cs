using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf.Reading;

public class PdfStamperTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==");

    private static string Latin1(byte[] pdf) => Encoding.GetEncoding("ISO-8859-1").GetString(pdf);

    private static byte[] RotatedDocument(int rotation, string cropBox = "") => new TestPdfBuilder()
        .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
        .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 600 800] >>")
        .Add(3, $"<< /Type /Page /Parent 2 0 R /Rotate {rotation.ToString(CultureInfo.InvariantCulture)} {cropBox} /Contents 4 0 R /Resources << /XObject << /Im0 5 0 R >> >> >>")
        .Add(4, "<< /Length 8 >>\nstream\n1 0 0 1 \nendstream")
        .Add(5, "<< /Type /XObject /Subtype /Form /BBox [0 0 1 1] /Length 0 >>\nstream\n\nendstream")
        .BuildClassic(root: 1);

    private static string StampContent(byte[] stamped, byte[] original)
    {
        var appended = Latin1(stamped).Substring(original.Length);
        var match = Regex.Match(appended, @"(q [^\n]* cm [^\n]* cm /\S+ Do Q)");
        match.Success.Should().BeTrue("the update must contain the stamp's drawing operators");
        return match.Groups[1].Value;
    }

    [Fact]
    public void Build_AppendsAnUpdateAndKeepsTheOriginalBytes()
    {
        var original = HtmlToPdf.Render("<p>Contract</p>");

        var stamped = new PdfStamper(original).AddImage(1, Png, 10, 20, 100, 50).Build();

        stamped.Length.Should().BeGreaterThan(original.Length);
        stamped.AsSpan(0, original.Length).SequenceEqual(original).Should().BeTrue("an incremental update only appends");
        var reader = PdfReader.Open(stamped);
        reader.PageCount.Should().Be(1);
        Latin1(stamped).Should().Contain("/Subtype /Image");
    }

    [Fact]
    public void Build_KeepsThePagesExistingResources()
    {
        var original = RotatedDocument(0);

        var stamped = new PdfStamper(original).AddImage(1, Png, 0, 0, 10, 10).Build();

        var appended = Latin1(stamped).Substring(original.Length);
        appended.Should().Contain("/Im0 5 0 R", "the page's own XObjects must stay reachable from the new page revision");
    }

    [Fact]
    public void Build_WrapsOriginalContentSoItsGraphicsStateCantLeak()
    {
        var original = RotatedDocument(0);

        var stamped = new PdfStamper(original).AddImage(1, Png, 0, 0, 10, 10).Build();

        var appended = Latin1(stamped).Substring(original.Length);
        appended.Should().MatchRegex(@"/Contents \[\d+ 0 R 4 0 R \d+ 0 R\]");
    }

    [Theory]
    [InlineData(0, "1 0 0 1 0 0 cm 100 0 0 50 10 730 cm")]
    [InlineData(90, "0 1 -1 0 600 0 cm 100 0 0 50 10 530 cm")]
    [InlineData(180, "-1 0 0 -1 600 800 cm 100 0 0 50 10 730 cm")]
    [InlineData(270, "0 -1 1 0 0 800 cm 100 0 0 50 10 530 cm")]
    public void AddImage_TopLeftCoordinatesOnTheDisplayedPage_MapThroughRotation(int rotation, string expectedOperators)
    {
        var original = RotatedDocument(rotation);

        var stamped = new PdfStamper(original).AddImage(1, Png, 10, 20, 100, 50).Build();

        StampContent(stamped, original).Should().Contain(expectedOperators);
    }

    [Fact]
    public void AddImage_CropBoxOrigin_OffsetsThePlacement()
    {
        var original = RotatedDocument(0, "/CropBox [50 100 550 700]");

        var stamped = new PdfStamper(original).AddImage(1, Png, 0, 0, 10, 10).Build();

        StampContent(stamped, original).Should().Contain("1 0 0 1 50 100 cm 10 0 0 10 0 590 cm");
    }

    [Fact]
    public void AddImage_Contain_FitsAndCentresTheImage()
    {
        var original = RotatedDocument(0);

        var stamped = new PdfStamper(original).AddImage(1, Png, 0, 0, 100, 50, PdfImageFit.Contain).Build();

        StampContent(stamped, original).Should().Contain("50 0 0 50 25 750 cm", "a square image in a 100x50 box is 50x50, centred");
    }

    [Fact]
    public void Build_SeveralImagesOnSeveralPages_AllLand()
    {
        var original = HtmlToPdf.Render("<div style=\"page-break-after:always\">a</div><div>b</div>");

        var stamped = new PdfStamper(original)
            .AddImage(1, Png, 0, 0, 10, 10)
            .AddImage(2, Png, 5, 5, 10, 10)
            .AddImage(2, Png, 50, 50, 10, 10)
            .Build();

        PdfReader.Open(stamped).PageCount.Should().Be(2);
        Regex.Matches(Latin1(stamped).Substring(original.Length), @" Do Q").Count.Should().Be(3);
    }

    [Fact]
    public void Build_XrefStreamInput_AppendsAnXrefStream()
    {
        var original = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 300 300] >>", inObjectStream: true)
            .Add(3, "<< /Type /Page /Parent 2 0 R >>", inObjectStream: true)
            .BuildWithXrefStream(root: 1, pngUpPredictor: true);

        var stamped = new PdfStamper(original).AddImage(1, Png, 0, 0, 10, 10).Build();

        Latin1(stamped).Substring(original.Length).Should().Contain("/Type /XRef");
        var reader = PdfReader.Open(stamped);
        reader.PageCount.Should().Be(1);
        reader.GetPage(1).Width.Should().Be(300);
    }

    [Fact]
    public void Build_StampingTwice_ChainsUpdates()
    {
        var once = new PdfStamper(HtmlToPdf.Render("<p>x</p>")).AddImage(1, Png, 0, 0, 10, 10).Build();

        var twice = new PdfStamper(once).AddImage(1, Png, 20, 20, 10, 10).Build();

        PdfReader.Open(twice).PageCount.Should().Be(1);
        Regex.Matches(Latin1(twice), @"startxref").Count.Should().Be(3);
    }

    [Fact]
    public void Build_SignedInput_KeepsTheEarlierSignatureValid()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Stamp Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var signed = PdfSigner.Sign(HtmlToPdf.Render("<p>signed</p>"), cert);

        var stamped = new PdfStamper(signed).AddImage(1, Png, 0, 0, 10, 10).Build();

        var text = Latin1(stamped);
        var byteRange = Regex.Match(text, @"/ByteRange \[(\d+) (\d+) (\d+) (\d+)\]");
        int r1 = int.Parse(byteRange.Groups[2].Value, CultureInfo.InvariantCulture);
        int r2 = int.Parse(byteRange.Groups[3].Value, CultureInfo.InvariantCulture);
        int r3 = int.Parse(byteRange.Groups[4].Value, CultureInfo.InvariantCulture);
        var hex = Regex.Match(text, @"/Contents <([0-9A-Fa-f]+)>").Groups[1].Value;
        var padded = Convert.FromHexString(hex);
        var cms = padded.AsSpan(0, DerTlvReader.ReadTotalLength(padded)).ToArray();
        var content = new byte[r1 + r3];
        Array.Copy(stamped, 0, content, 0, r1);
        Array.Copy(stamped, r2, content, r1, r3);
        var signedCms = new SignedCms(new ContentInfo(content), detached: true);
        signedCms.Decode(cms);

        Action verify = () => signedCms.CheckSignature(verifySignatureOnly: true);

        verify.Should().NotThrow("stamping appends after the signed bytes instead of rewriting them");
    }

    [Fact]
    public void BuildToStream_WritesTheSameBytesAsBuild()
    {
        var stamper = new PdfStamper(RotatedDocument(90)).AddImage(1, Png, 10, 20, 30, 40);
        var output = new MemoryStream();

        stamper.Build(output);

        output.ToArray().Should().Equal(stamper.Build());
    }

    [Fact]
    public void Build_LargeInput_AllocatesOnlyTheUpdate()
    {
        const int contentLength = 32 * 1024 * 1024;
        var original = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 300 300] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>")
            .Add(4, "<< /Length " + contentLength.ToString(CultureInfo.InvariantCulture) + " >>\nstream\n"
                    + new string(' ', contentLength) + "\nendstream")
            .BuildClassic(root: 1);
        var stamper = new PdfStamper(original).AddImage(1, Png, 0, 0, 10, 10);

        long before = GC.GetAllocatedBytesForCurrentThread();
        stamper.Build(Stream.Null);
        long toStream = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        var stamped = stamper.Build();
        long toArray = GC.GetAllocatedBytesForCurrentThread() - before;

        toStream.Should().BeLessThan(1024 * 1024);
        toArray.Should().BeLessThan(stamped.Length + 1024 * 1024);
    }

    [Fact]
    public void Build_NonUtf8ResourceNames_SurviveByteForByte()
    {
        var original = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 300 300] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << /Font << /F#E9 5 0 R /F#E8 5 0 R >> " +
                    "/ColorSpace << /CS0 [/Separation /PANTONE#AE#20Red /DeviceCMYK 6 0 R] >> >> >>")
            .Add(4, "<< /Length 0 >>\nstream\n\nendstream")
            .Add(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")
            .Add(6, "<< /FunctionType 2 /Domain [0 1] /C0 [0 0 0 0] /C1 [0 1 1 0] /N 1 >>")
            .BuildClassic(root: 1);

        var stamped = new PdfStamper(original).AddImage(1, Png, 0, 0, 10, 10).Build();

        var appended = Latin1(stamped).Substring(original.Length);
        appended.Should().Contain("/F#E9 5 0 R").And.Contain("/F#E8 5 0 R").And.Contain("/PANTONE#AE#20Red");
    }

    [Fact]
    public void Build_RealNumbersInThePageDictionary_KeepTheirPrecision()
    {
        var original = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.27559 841.88976] /UserData [-0.000049] >>")
            .BuildClassic(root: 1);

        var stamped = new PdfStamper(original).AddImage(1, Png, 0, 0, 10, 10).Build();

        Latin1(stamped).Substring(original.Length).Should().Contain("595.27559").And.Contain("841.88976").And.Contain("-0.000049");
    }

    [Fact]
    public void Build_StampsAreMarkedAsArtifactsForTaggedDocuments()
    {
        var original = RotatedDocument(0);

        var stamped = new PdfStamper(original).AddImage(1, Png, 0, 0, 10, 10).Build();

        Latin1(stamped).Substring(original.Length).Should().MatchRegex(@"/Artifact BMC q [^\n]* Do Q EMC");
    }

    [Fact]
    public void Build_SameImageOnSeveralPages_IsEmbeddedOnce()
    {
        var original = HtmlToPdf.Render("<div style=\"page-break-after:always\">a</div><div style=\"page-break-after:always\">b</div><div>c</div>");

        var stamped = new PdfStamper(original)
            .AddImage(1, Png, 0, 0, 10, 10)
            .AddImage(2, Png, 0, 0, 10, 10)
            .AddImage(3, Png, 0, 0, 10, 10)
            .Build();

        var appended = Latin1(stamped).Substring(original.Length);
        Regex.Matches(appended, @"/SMask \d+ 0 R").Count.Should().Be(1);
        Regex.Matches(appended, @"/Subtype /Image").Count.Should().Be(2, "one image plus its soft mask");
    }

    [Theory]
    [InlineData("/Reference [<< /Type /SigRef /TransformMethod /DocMDP /TransformParams << /Type /TransformParams /P 1 /V /1.2 >> >>]")]
    [InlineData("/Reference [<< /Type /SigRef /TransformMethod /DocMDP /TransformParams << /Type /TransformParams /P 2 /V /1.2 >> >>]")]
    [InlineData("/Reference [<< /Type /SigRef /TransformMethod /DocMDP /TransformParams << /Type /TransformParams /P 3 /V /1.2 >> >>]")]
    [InlineData("/Reference [<< /Type /SigRef /TransformMethod /DocMDP /TransformParams << /Type /TransformParams /V /1.2 >> >>]")]
    [InlineData("")]
    public void Constructor_CertifiedDocument_ThrowsNotSupported(string reference)
    {
        var original = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /Perms << /DocMDP 4 0 R >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 300 300] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>")
            .Add(4, $"<< /Type /Sig {reference} >>")
            .BuildClassic(root: 1);

        Action act = () => new PdfStamper(original);

        act.Should().Throw<NotSupportedException>().WithMessage("*certif*");
    }

    [Fact]
    public void Constructor_HostileTrailerSize_ThrowsPdfFormatException()
    {
        var original = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 300 300] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>")
            .BuildClassic(root: 1);
        var hostile = Encoding.GetEncoding("ISO-8859-1").GetBytes(
            Latin1(original).Replace("/Size 4", "/Size 2147483647"));

        Action act = () => new PdfStamper(hostile).AddImage(1, Png, 0, 0, 10, 10).Build();

        act.Should().Throw<PdfFormatException>();
    }

    [Theory]
    [InlineData(1e300, 0, 10, 10)]
    [InlineData(0, 0, 1e-9, 10)]
    [InlineData(0, 0, 10, 2e6)]
    public void AddImage_OutOfRangeGeometry_Throws(double x, double y, double width, double height)
    {
        var stamper = new PdfStamper(HtmlToPdf.Render("<p>x</p>"));

        Action act = () => stamper.AddImage(1, Png, x, y, width, height);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddImage_PageOutOfRange_Throws()
    {
        var stamper = new PdfStamper(HtmlToPdf.Render("<p>x</p>"));

        Action act = () => stamper.AddImage(2, Png, 0, 0, 10, 10);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, -1)]
    [InlineData(double.NaN, 10)]
    public void AddImage_InvalidSize_Throws(double width, double height)
    {
        var stamper = new PdfStamper(HtmlToPdf.Render("<p>x</p>"));

        Action act = () => stamper.AddImage(1, Png, 0, 0, width, height);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddImage_NotAnImage_Throws()
    {
        var stamper = new PdfStamper(HtmlToPdf.Render("<p>x</p>"));

        Action act = () => stamper.AddImage(1, new byte[] { 1, 2, 3, 4 }, 0, 0, 10, 10);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_EncryptedPdf_ThrowsNotSupported()
    {
        var encrypted = HtmlToPdf.Render("<p>x</p>", new PdfEncryption { OwnerPassword = "o" });

        Action act = () => new PdfStamper(encrypted);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Build_NoImages_ReturnsTheInputUnchanged()
    {
        var original = HtmlToPdf.Render("<p>x</p>");

        new PdfStamper(original).Build().Should().Equal(original);
    }
}
