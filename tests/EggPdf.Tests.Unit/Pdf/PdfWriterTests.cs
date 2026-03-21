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
}
