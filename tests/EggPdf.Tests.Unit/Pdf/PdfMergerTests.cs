using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfMergerTests
{
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC";

    private static byte[] MakePdf(string text, bool compress, PdfEncryption? encryption = null)
    {
        var doc = new PdfDocument { CompressContentStreams = compress, Encryption = encryption };
        var page = doc.AddPage(595, 842);
        page.AddText(text, 50, 700, "Helvetica", 12);
        return doc.ToByteArray();
    }

    private static string Latin1(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    private static string StreamContents(byte[] pdf)
    {
        var text = Latin1(pdf);
        var contents = new StringBuilder();
        foreach (Match match in Regex.Matches(text, "stream\r?\n"))
        {
            int start = match.Index + match.Length;
            int end = text.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end < 0) continue;
            var body = pdf.Skip(start).Take(end - start).ToArray();
            contents.Append(TryInflate(body) ?? Latin1(body)).Append('\n');
        }
        return contents.ToString();
    }

    private static string? TryInflate(byte[] body)
    {
        try
        {
            using var input = new MemoryStream(body);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return Latin1(output.ToArray());
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static string Foreign(string objects)
        => "%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" + objects + "trailer\n<< /Root 1 0 R >>\n%%EOF";

    [Fact]
    public void Merge_UncompressedInputs_KeepsBothPagesText()
    {
        var merged = new PdfMerger().Add(MakePdf("first document", compress: false)).Add(MakePdf("second document", compress: false)).Build();

        var text = StreamContents(merged);
        text.Should().Contain("(first document) Tj");
        text.Should().Contain("(second document) Tj");
        PdfReader.Open(merged).PageCount.Should().Be(2);
    }

    [Fact]
    public void Merge_CompressedInputs_KeepsBothPagesText()
    {
        var merged = new PdfMerger().Add(MakePdf("first compressed", compress: true)).Add(MakePdf("second compressed", compress: true)).Build();

        var text = StreamContents(merged);
        text.Should().Contain("(first compressed) Tj");
        text.Should().Contain("(second compressed) Tj");
    }

    [Fact]
    public void Merge_EncryptedInput_ThrowsInsteadOfProducingGarbage()
    {
        var merger = new PdfMerger()
            .Add(MakePdf("secret", compress: false, encryption: new PdfEncryption { OwnerPassword = "owner" }))
            .Add(MakePdf("plain", compress: false));

        var act = () => merger.Build();

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Merge_ForeignPdf_IndirectEncryptReference_IsRefused()
    {
        var foreign = Encoding.ASCII.GetBytes(
            "%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\n" +
            "trailer\n<< /Root 1 0 R /Encrypt 9 0 R /ID [<00> <00>] >>\n%%EOF");
        var merger = new PdfMerger().Add(foreign).Add(MakePdf("plain", compress: false));

        var act = () => merger.Build();

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Merge_RealRendersWithEmbeddedFonts_CopiesEveryFontProgram()
    {
        var first = HtmlToPdf.Render("<html><body><p>Giấy Chứng Nhận số một</p></body></html>");
        var second = HtmlToPdf.Render("<html><body><p>Trang thứ hai</p></body></html>");
        int firstFonts = Regex.Matches(Latin1(first), "/FontFile2").Count;
        int secondFonts = Regex.Matches(Latin1(second), "/FontFile2").Count;
        firstFonts.Should().BeGreaterThan(0, "the inputs must embed fonts for this test to mean anything");

        var merged = new PdfMerger().Add(first).Add(second).Build();

        Regex.Matches(Latin1(merged), "/FontFile2").Count.Should().Be(firstFonts + secondFonts,
            "a merged page that names /F1 must still find the font program it was drawn with");
        PdfReader.Open(merged).PageCount.Should().Be(2);
    }

    [Fact]
    public void Merge_PageWithImage_CopiesTheImage()
    {
        var withImage = HtmlToPdf.Render($"<html><body><img src='data:image/png;base64,{OnePixelPng}' width='40' height='40'></body></html>");
        Latin1(withImage).Should().Contain("/Subtype /Image");

        var merged = new PdfMerger().Add(MakePdf("first", compress: false)).Add(withImage).Build();

        Latin1(merged).Should().Contain("/Subtype /Image");
    }

    [Fact]
    public void Merge_PagesOfDifferentSizes_KeepsEachPageOwnMediaBox()
    {
        var mixed = new PdfDocument();
        mixed.AddPage(595.28f, 841.89f).AddText("a4 page", 50, 700, "Helvetica", 12);
        mixed.AddPage(612f, 792f).AddText("letter page", 50, 700, "Helvetica", 12);

        var merged = new PdfMerger().Add(mixed.ToByteArray()).Add(MakePdf("tail", false)).Build();

        var reader = PdfReader.Open(merged);
        reader.PageCount.Should().Be(3);
        reader.GetPage(1).Width.Should().BeApproximately(595.28, 0.01);
        reader.GetPage(2).Width.Should().BeApproximately(612, 0.01);
        reader.GetPage(2).Height.Should().BeApproximately(792, 0.01);
    }

    [Fact]
    public void Merge_MultiStreamContentsArray_KeepsAllStreams()
    {
        var foreign = Encoding.ASCII.GetBytes(Foreign(
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents [5 0 R 6 0 R] >>\nendobj\n" +
            "5 0 obj\n<< /Length 32 >>\nstream\nBT /F1 12 Tf (first part) Tj ET\nendstream\nendobj\n" +
            "6 0 obj\n<< /Length 33 >>\nstream\nBT /F1 12 Tf (second part) Tj ET\nendstream\nendobj\n"));

        var text = StreamContents(new PdfMerger().Add(foreign).Add(MakePdf("tail", compress: false)).Build());

        text.Should().Contain("(first part) Tj");
        text.Should().Contain("(second part) Tj");
        text.Should().Contain("(tail) Tj");
    }

    [Fact]
    public void Merge_PageWithInheritedMediaBoxAndResources_KeepsThemOnTheCopiedPage()
    {
        var foreign = Encoding.ASCII.GetBytes(Foreign(
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 500 700] " +
            "/Resources << /Font << /F1 7 0 R >> >> >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>\nendobj\n" +
            "4 0 obj\n<< /Length 29 >>\nstream\nBT /F1 12 Tf (inherited) Tj ET\nendstream\nendobj\n" +
            "7 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>\nendobj\n"));

        var merged = new PdfMerger().Add(foreign).Add(MakePdf("tail", compress: false)).Build();

        var reader = PdfReader.Open(merged);
        reader.GetPage(1).Width.Should().BeApproximately(500, 0.01);
        reader.GetPage(1).Height.Should().BeApproximately(700, 0.01);
        Latin1(merged).Should().Contain("/BaseFont /Courier", "the font inherited from the page tree is copied with the page");
        StreamContents(merged).Should().Contain("(inherited) Tj");
    }

    [Fact]
    public void Merge_LinkToAnotherPageOfTheSameInput_PointsToTheCopiedPage()
    {
        var foreign = Encoding.ASCII.GetBytes(Foreign(
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 /MediaBox [0 0 500 700] >>\nendobj\n" +
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /Annots [5 0 R] >>\nendobj\n" +
            "4 0 obj\n<< /Type /Page /Parent 2 0 R >>\nendobj\n" +
            "5 0 obj\n<< /Type /Annot /Subtype /Link /Rect [0 0 10 10] /Dest [4 0 R /Fit] >>\nendobj\n"));

        var merged = Latin1(new PdfMerger().Add(MakePdf("head", false)).Add(foreign).Build());

        var link = Regex.Match(merged, @"/Dest \[(\d+) 0 R /Fit\]");
        link.Success.Should().BeTrue("the link keeps its destination page");
        var target = link.Groups[1].Value;
        Regex.IsMatch(merged, $@"(^|\n){target} 0 obj\n<<[^\n]*/Type /Page\b").Should().BeTrue("the destination is the copied page, not a dangling reference");
    }

    [Fact]
    public void Merge_TwoLargeDocuments_StreamsWithBoundedMemory()
    {
        byte[] Large(string label)
        {
            var doc = new PdfDocument();
            for (int i = 0; i < 1000; i++)
                doc.AddPage(595, 842).AddText($"{label} {i}", 50, 700, "Helvetica", 12);
            return doc.ToByteArray();
        }

        var merger = new PdfMerger().Add(Large("left")).Add(Large("right"));
        using var output = new MemoryStream();

        long before = GC.GetAllocatedBytesForCurrentThread();
        merger.WriteTo(output);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before - output.Capacity;

        PdfReader.Open(output.ToArray()).PageCount.Should().Be(2000);
        allocated.Should().BeLessThan(64L * 1024 * 1024, "merging copies objects one at a time instead of building the output in memory");
    }
}
