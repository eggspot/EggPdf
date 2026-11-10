using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfWriterTests
{
    private const float A4Width = 595.28f, A4Height = 841.89f;

    private static string SecondTextObject(string pdfText)
    {
        var firstBt = pdfText.IndexOf("BT", StringComparison.Ordinal);
        var secondBt = pdfText.IndexOf("BT", firstBt + 2, StringComparison.Ordinal);
        secondBt.Should().BeGreaterThan(0, "there should be a second BT block");
        return pdfText.Substring(secondBt);
    }

    [Fact]
    public void EmptyDocument_ProducesValidPdf()
    {
        var doc = new PdfDocument();
        doc.AddPage(A4Width, A4Height);

        var bytes = doc.ToByteArray();

        bytes.Should().NotBeEmpty();
        var header = Encoding.ASCII.GetString(bytes, 0, 8);
        header.Should().StartWith("%PDF-1.7");
    }

    [Fact]
    public void EmptyDocument_EndsWithEof()
    {
        var doc = new PdfDocument();
        doc.AddPage(A4Width, A4Height);

        var bytes = doc.ToByteArray();
        var tail = Encoding.ASCII.GetString(bytes, bytes.Length - 6, 6).Trim();
        tail.Should().EndWith("%%EOF");
    }

    [Fact]
    public void EmptyDocument_HasXrefTable()
    {
        var doc = new PdfDocument();
        doc.AddPage(A4Width, A4Height);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().Contain("xref");
        text.Should().Contain("trailer");
        text.Should().Contain("startxref");
    }

    [Fact]
    public void Page_HasMediaBox()
    {
        var doc = new PdfDocument();
        doc.AddPage(A4Width, A4Height);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().Contain("/MediaBox");
    }

    [Fact]
    public void TextContent_AppearInContentStream()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(A4Width, A4Height);
        page.AddText("Hello World", 72, 720, "Helvetica", 12);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().Contain("/Helvetica");
        text.Should().Contain("BT");
        text.Should().Contain("ET");
    }

    [Fact]
    public void Rectangle_AppearsInContentStream()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(A4Width, A4Height);
        page.AddRectangle(72, 700, 200, 50, 1.0f, 0, 0);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().Contain("1.00 0.00 0.00 rg", "the rectangle is filled red");
        text.Should().Contain("72.00 700.00 200.00 50.00 re f");
    }

    [Fact]
    public void MultiplePages_AllPresent()
    {
        var doc = new PdfDocument();
        doc.AddPage(A4Width, A4Height);
        doc.AddPage(A4Width, A4Height);
        doc.AddPage(A4Width, A4Height);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().Contain("/Count 3");
    }

    [Fact]
    public void WriteToStream_MatchesToByteArray()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(A4Width, A4Height);
        page.AddText("Test", 72, 720, "Helvetica", 12);

        var byteResult = doc.ToByteArray();

        using var ms = new MemoryStream();
        doc.WriteTo(ms);
        var streamResult = ms.ToArray();

        streamResult.Should().Equal(byteResult);
    }

    [Fact]
    public void DocumentInfo_Title_Present()
    {
        var doc = new PdfDocument();
        doc.Title = "My Document";
        doc.AddPage(A4Width, A4Height);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().Contain("My Document");
    }

    [Fact]
    public void LinkAnnotation_Present()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(A4Width, A4Height);
        page.AddLink(72, 700, 200, 20, "https://example.com");

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().Contain("/Annot");
        text.Should().Contain("https://example.com");
    }

    [Fact]
    public void AddText_WithoutLetterSpacing_AlwaysEmitsTcZero()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595f, 842f);
        page.AddText("HEADER", 10, 800, "Helvetica", 14, letterSpacing: 2.25f);
        page.AddText("Body text", 10, 780, "Helvetica", 12);

        var pdfText = Encoding.Latin1.GetString(doc.ToByteArray());

        SecondTextObject(pdfText).Should().Contain("0.00 Tc",
            "Tc persists across BT/ET, so text without letter-spacing must reset it or it inherits the header's spacing");
    }

    [Fact]
    public void AddText_WithoutWordSpacing_AlwaysEmitsTwZero()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595f, 842f);
        page.AddText("HEADER", 10, 800, "Helvetica", 14, wordSpacing: 5f);
        page.AddText("Body text", 10, 780, "Helvetica", 12);

        var pdfText = Encoding.Latin1.GetString(doc.ToByteArray());

        SecondTextObject(pdfText).Should().Contain("0.00 Tw", "word-spacing must be explicitly reset to 0");
    }

    [Fact]
    public void AddTextCID_NoDuplicateTfOperator()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595f, 842f);
        page.AddTextCID(new ushort[] { 1, 2, 3 }, 10, 800, "Arial", 12);

        var pdfText = Encoding.Latin1.GetString(doc.ToByteArray());
        var btIdx = pdfText.IndexOf("BT", StringComparison.Ordinal);
        btIdx.Should().BeGreaterThan(0);
        var etIdx = pdfText.IndexOf("ET", btIdx, StringComparison.Ordinal);
        var textObject = pdfText.Substring(btIdx, etIdx - btIdx);

        Regex.Matches(textObject, @"\bTf\b").Count.Should().Be(1,
            "a second, redundant Tf in the same text object may confuse PDF processors");
    }
}
