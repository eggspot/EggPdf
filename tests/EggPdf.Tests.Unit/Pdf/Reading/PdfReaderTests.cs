using System;
using System.Text;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf.Reading;

public class PdfReaderTests
{
    private static TestPdfBuilder TwoPageDocument(bool compressPages = false) => new TestPdfBuilder()
        .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
        .Add(2, "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 /MediaBox [0 0 612 792] >>", compressPages)
        .Add(3, "<< /Type /Page /Parent 2 0 R >>", compressPages)
        .Add(4, "<< /Type /Page /Parent 2 0 R /Rotate 90 /CropBox [10 20 210 120] >>", compressPages);

    [Fact]
    public void Open_EggPdfOutput_ReadsPagesAndSizes()
    {
        var pdf = HtmlToPdf.Render("<div style=\"page-break-after:always\">a</div><div style=\"page-break-after:always\">b</div><div>c</div>");

        var reader = PdfReader.Open(pdf);

        reader.PageCount.Should().Be(3);
        reader.IsEncrypted.Should().BeFalse();
        var page = reader.GetPage(1);
        page.Width.Should().BeApproximately(595.28, 0.5);
        page.Height.Should().BeApproximately(841.89, 0.5);
        page.Rotation.Should().Be(0);
    }

    [Fact]
    public void Open_ClassicXref_InheritsMediaBoxFromPageTree()
    {
        var reader = PdfReader.Open(TwoPageDocument().BuildClassic(root: 1));

        reader.PageCount.Should().Be(2);
        var page = reader.GetPage(1);
        page.PageNumber.Should().Be(1);
        page.Width.Should().Be(612);
        page.Height.Should().Be(792);
    }

    [Fact]
    public void GetPage_RotatedPageWithCropBox_ReportsSizeAsDisplayed()
    {
        var reader = PdfReader.Open(TwoPageDocument().BuildClassic(root: 1));

        var page = reader.GetPage(2);

        page.Rotation.Should().Be(90);
        page.Width.Should().Be(100, "a 200x100 crop box rotated 90 degrees is displayed 100 wide");
        page.Height.Should().Be(200);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Open_XrefStreamAndObjectStream_ReadsPages(bool pngUpPredictor)
    {
        var pdf = TwoPageDocument(compressPages: true).BuildWithXrefStream(root: 1, pngUpPredictor);

        var reader = PdfReader.Open(pdf);

        reader.PageCount.Should().Be(2);
        reader.GetPage(2).Rotation.Should().Be(90);
    }

    [Fact]
    public void Open_HybridFileWithXRefStm_ReadsObjectsFromTheStream()
    {
        var pdf = TwoPageDocument(compressPages: true).BuildWithXrefStream(root: 1, pngUpPredictor: false, hybrid: true);

        PdfReader.Open(pdf).PageCount.Should().Be(2);
    }

    [Fact]
    public void Open_IncrementalUpdate_UsesTheLatestRevision()
    {
        var original = TwoPageDocument().BuildClassic(root: 1);
        var updated = TestPdfBuilder.AppendUpdate(original, root: 1, size: 5,
            (2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 300 400] >>"));

        var reader = PdfReader.Open(updated);

        reader.PageCount.Should().Be(1);
        reader.GetPage(1).Width.Should().Be(300);
    }

    [Fact]
    public void Open_WrongStartXrefOffset_RecoversByScanningObjects()
    {
        var pdf = TwoPageDocument().BuildClassic(root: 1);
        var text = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);
        var broken = text.Substring(0, text.LastIndexOf("startxref", StringComparison.Ordinal)) + "startxref\n999999\n%%EOF\n";

        var reader = PdfReader.Open(Encoding.GetEncoding("ISO-8859-1").GetBytes(broken));

        reader.PageCount.Should().Be(2);
    }

    [Fact]
    public void Open_PrevChainPointingToItself_DoesNotLoop()
    {
        var original = TwoPageDocument().BuildClassic(root: 1);
        var text = Encoding.GetEncoding("ISO-8859-1").GetString(original);
        int xref = text.LastIndexOf("\nxref", StringComparison.Ordinal) + 1;
        var looped = text.Replace("/Root 1 0 R", "/Root 1 0 R /Prev " + xref.ToString(System.Globalization.CultureInfo.InvariantCulture));

        PdfReader.Open(Encoding.GetEncoding("ISO-8859-1").GetBytes(looped)).PageCount.Should().Be(2);
    }

    [Fact]
    public void Open_PageTreeCycle_ThrowsPdfFormatException()
    {
        var pdf = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Pages /Kids [2 0 R] /Count 1 >>")
            .BuildClassic(root: 1);

        Action act = () => PdfReader.Open(pdf);

        act.Should().Throw<PdfFormatException>();
    }

    [Fact]
    public void Open_DeeplyNestedArrays_ThrowsPdfFormatException()
    {
        var pdf = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /Deep " + new string('[', 5000) + new string(']', 5000) + " >>")
            .Add(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .BuildClassic(root: 1);

        Action act = () => PdfReader.Open(pdf);

        act.Should().Throw<PdfFormatException>();
    }

    [Fact]
    public void Open_DecompressionBombInObjectStream_ThrowsPdfFormatException()
    {
        var bomb = TestPdfBuilder.Zlib(new byte[70 * 1024 * 1024]);
        var latin1 = Encoding.GetEncoding("ISO-8859-1");
        var pdf = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", inObjectStream: true)
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] >>")
            .BuildWithXrefStream(root: 1, pngUpPredictor: false);
        var text = latin1.GetString(pdf);
        int streamStart = text.IndexOf("/Type /ObjStm", StringComparison.Ordinal);
        int dataStart = text.IndexOf("stream\n", streamStart, StringComparison.Ordinal) + 7;
        int dataEnd = text.IndexOf("\nendstream", dataStart, StringComparison.Ordinal);
        var lengthKey = text.IndexOf("/Length ", streamStart, StringComparison.Ordinal);
        var lengthEnd = text.IndexOf(" >>", lengthKey, StringComparison.Ordinal);
        var prefix = text.Substring(0, lengthKey) + "/Length " + bomb.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                     + text.Substring(lengthEnd, dataStart - lengthEnd);
        var withBomb = new byte[latin1.GetByteCount(prefix) + bomb.Length + (pdf.Length - dataEnd)];
        latin1.GetBytes(prefix).CopyTo(withBomb, 0);
        bomb.CopyTo(withBomb, latin1.GetByteCount(prefix));
        Array.Copy(pdf, dataEnd, withBomb, latin1.GetByteCount(prefix) + bomb.Length, pdf.Length - dataEnd);

        Action act = () => PdfReader.Open(withBomb);

        act.Should().Throw<PdfFormatException>();
    }

    [Fact]
    public void Open_EncryptedEggPdfOutput_ReportsEncryptionAndStillCountsPages()
    {
        var pdf = HtmlToPdf.Render("<p>secret</p>", new EggPdf.Pdf.PdfEncryption { OwnerPassword = "o", UserPassword = "u" });

        var reader = PdfReader.Open(pdf);

        reader.IsEncrypted.Should().BeTrue();
        reader.PageCount.Should().Be(1);
    }

    [Fact]
    public void GetPageCount_EggPdfOutput_MatchesRenderedPages()
    {
        var pdf = HtmlToPdf.Render("<div style=\"page-break-after:always\">a</div><div>b</div>");

        PdfReader.GetPageCount(pdf).Should().Be(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void GetPage_OutOfRange_ThrowsArgumentOutOfRange(int pageNumber)
    {
        var reader = PdfReader.Open(TwoPageDocument().BuildClassic(root: 1));

        Action act = () => reader.GetPage(pageNumber);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 1, 2, 3, 4, 5 })]
    public void Open_NotAPdf_ThrowsPdfFormatException(byte[] bytes)
    {
        Action act = () => PdfReader.Open(bytes);

        act.Should().Throw<PdfFormatException>();
    }

    [Fact]
    public void Open_Null_ThrowsArgumentNull()
    {
        Action act = () => PdfReader.Open(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PdfFormatException_IsAFormatException()
    {
        new PdfFormatException("x").Should().BeAssignableTo<FormatException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Open_RandomlyCorruptedPdf_ThrowsOnlyDocumentedExceptions(int seed)
    {
        var sources = new[]
        {
            HtmlToPdf.Render("<p>fuzz</p>"),
            TwoPageDocument(compressPages: true).BuildWithXrefStream(root: 1, pngUpPredictor: true),
            TwoPageDocument().BuildClassic(root: 1),
        };
        var random = new Random(seed);
        foreach (var source in sources)
        {
            for (int round = 0; round < 300; round++)
            {
                var mutated = (byte[])source.Clone();
                int edits = random.Next(1, 8);
                for (int e = 0; e < edits; e++)
                    mutated[random.Next(mutated.Length)] = (byte)random.Next(256);
                if (random.Next(4) == 0)
                    Array.Resize(ref mutated, random.Next(1, mutated.Length));

                try
                {
                    var reader = PdfReader.Open(mutated);
                    for (int p = 1; p <= reader.PageCount; p++) reader.GetPage(p);
                }
                catch (PdfFormatException) { }
                catch (NotSupportedException) { }
            }
        }
    }
}
