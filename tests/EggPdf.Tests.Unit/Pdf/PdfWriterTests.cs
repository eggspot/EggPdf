using System.IO;
using System.Text;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfWriterTests
{
    [Fact]
    public void EmptyDocument_ProducesValidPdf()
    {
        var doc = new PdfDocument();
        doc.AddPage(595.28f, 841.89f); // A4

        var bytes = doc.ToByteArray();

        bytes.Should().NotBeEmpty();
        var header = Encoding.ASCII.GetString(bytes, 0, 8);
        header.Should().StartWith("%PDF-1.7");
    }

    [Fact]
    public void EmptyDocument_EndsWithEof()
    {
        var doc = new PdfDocument();
        doc.AddPage(595.28f, 841.89f);

        var bytes = doc.ToByteArray();
        var tail = Encoding.ASCII.GetString(bytes, bytes.Length - 6, 6).Trim();
        tail.Should().EndWith("%%EOF");
    }

    [Fact]
    public void EmptyDocument_HasXrefTable()
    {
        var doc = new PdfDocument();
        doc.AddPage(595.28f, 841.89f);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().Contain("xref");
        text.Should().Contain("trailer");
        text.Should().Contain("startxref");
    }

    [Fact]
    public void Page_HasMediaBox()
    {
        var doc = new PdfDocument();
        doc.AddPage(595.28f, 841.89f);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().Contain("/MediaBox");
    }

    [Fact]
    public void TextContent_AppearInContentStream()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595.28f, 841.89f);
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
        var page = doc.AddPage(595.28f, 841.89f);
        page.AddRectangle(72, 700, 200, 50, 1.0f, 0, 0);

        var bytes = doc.ToByteArray();
        bytes.Should().NotBeEmpty();
        // Should contain rectangle operator
        var text = Encoding.ASCII.GetString(bytes);
        text.Should().Contain("re");
    }

    [Fact]
    public void MultiplPages_AllPresent()
    {
        var doc = new PdfDocument();
        doc.AddPage(595.28f, 841.89f);
        doc.AddPage(595.28f, 841.89f);
        doc.AddPage(595.28f, 841.89f);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().Contain("/Count 3");
    }

    [Fact]
    public void WriteToStream_MatchesToByteArray()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595.28f, 841.89f);
        page.AddText("Test", 72, 720, "Helvetica", 12);

        var byteResult = doc.ToByteArray();

        using var ms = new MemoryStream();
        doc.WriteTo(ms);
        var streamResult = ms.ToArray();

        streamResult.Should().BeEquivalentTo(byteResult);
    }

    [Fact]
    public void DocumentInfo_Title_Present()
    {
        var doc = new PdfDocument();
        doc.Title = "My Document";
        doc.AddPage(595.28f, 841.89f);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().Contain("My Document");
    }

    [Fact]
    public void LinkAnnotation_Present()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595.28f, 841.89f);
        page.AddLink(72, 700, 200, 20, "https://example.com");

        var text = Encoding.ASCII.GetString(doc.ToByteArray());
        text.Should().Contain("/Annot");
        text.Should().Contain("https://example.com");
    }

    [Fact]
    public void AddText_WithoutLetterSpacing_AlwaysEmitsTcZero()
    {
        // Regression: Tc persists across BT/ET boundaries in PDF spec.
        // A previous AddText with letterSpacing != 0 would "infect" all subsequent
        // text with extra character spacing unless Tc is explicitly reset to 0.
        var doc = new PdfDocument();
        var page = doc.AddPage(595f, 842f);

        // First call: non-zero letter-spacing sets Tc
        page.AddText("HEADER", 10, 800, "Helvetica", 14, letterSpacing: 2.25f);
        // Second call: no letter-spacing — must explicitly reset Tc to 0
        page.AddText("Body text", 10, 780, "Helvetica", 12);

        var pdfText = Encoding.Latin1.GetString(doc.ToByteArray());

        // Second BT block must contain "0.00 Tc" to reset character spacing
        var firstBt = pdfText.IndexOf("BT");
        var secondBt = pdfText.IndexOf("BT", firstBt + 2);
        secondBt.Should().BeGreaterThan(0, "there should be a second BT block");
        var secondBlock = pdfText.Substring(secondBt);
        secondBlock.Should().Contain("0.00 Tc", "letter-spacing must be explicitly reset to 0");
    }

    [Fact]
    public void AddText_WithoutWordSpacing_AlwaysEmitsTwZero()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595f, 842f);

        page.AddText("HEADER", 10, 800, "Helvetica", 14, wordSpacing: 5f);
        page.AddText("Body text", 10, 780, "Helvetica", 12);

        var pdfText = Encoding.Latin1.GetString(doc.ToByteArray());

        var firstBt = pdfText.IndexOf("BT");
        var secondBt = pdfText.IndexOf("BT", firstBt + 2);
        secondBt.Should().BeGreaterThan(0, "there should be a second BT block");
        var secondBlock = pdfText.Substring(secondBt);
        secondBlock.Should().Contain("0.00 Tw", "word-spacing must be explicitly reset to 0");
    }

    [Fact]
    public void AddTextCID_NoDuplicateTfOperator()
    {
        // Regression: AddTextCID was emitting two consecutive Tf operators.
        // The second one is redundant and may confuse PDF processors.
        var doc = new PdfDocument();
        var page = doc.AddPage(595f, 842f);
        page.AddTextCID(new ushort[] { 1, 2, 3 }, 10, 800, "Arial", 12);

        var pdfText = Encoding.Latin1.GetString(doc.ToByteArray());

        // Extract content between BT and ET
        var btIdx = pdfText.IndexOf("BT");
        var etIdx = pdfText.IndexOf("ET", btIdx);
        btIdx.Should().BeGreaterThan(0);
        var block = pdfText.Substring(btIdx, etIdx - btIdx);

        // Count standalone "Tf" tokens (preceded by space/number and followed by space/newline)
        var tfCount = 0;
        var search = 0;
        while (true)
        {
            var idx = block.IndexOf("Tf", search);
            if (idx < 0) break;
            tfCount++;
            search = idx + 2;
        }
        tfCount.Should().Be(1, "each BT block should have exactly one Tf operator");
    }
}
