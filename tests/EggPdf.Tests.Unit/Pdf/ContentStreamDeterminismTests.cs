using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>
/// PDF output must be byte-identical for the same input regardless of host
/// platform. StringBuilder.AppendLine emits Environment.NewLine, which made
/// Windows output carry CRLF in content streams while Linux carried LF —
/// breaking reproducibility for hashing and signing workflows.
/// </summary>
public class ContentStreamDeterminismTests
{
    private static byte[] Render()
    {
        var doc = new PdfDocument { CompressContentStreams = false };
        var page = doc.AddPage(595, 842);
        page.AddText("determinism", 50, 700, "Helvetica", 12);
        page.AddRectangle(10, 10, 100, 50, 1, 0, 0);
        return doc.ToByteArray();
    }

    [Fact]
    public void ContentStream_UsesLfOnly_NeverCrLf()
    {
        var text = System.Text.Encoding.Latin1.GetString(Render());

        text.Should().Contain(") Tj ET\n",
            "content-stream lines must terminate with a literal LF");
        text.Should().NotContain("\r\n",
            "CRLF would make output differ between Windows and Linux hosts");
        text.Should().NotContain("\r",
            "no stray carriage returns may reach the content stream");
    }

    [Fact]
    public void SameInput_ProducesIdenticalContentStreamBytes()
    {
        // Guards the whole pipeline, not just line endings (the document ID
        // and timestamps live outside the content stream).
        var a = System.Text.Encoding.Latin1.GetString(Render());
        var b = System.Text.Encoding.Latin1.GetString(Render());

        static string ContentOf(string pdf)
        {
            int start = pdf.IndexOf("stream\n", System.StringComparison.Ordinal);
            int end = pdf.IndexOf("endstream", start, System.StringComparison.Ordinal);
            return pdf.Substring(start, end - start);
        }

        ContentOf(a).Should().Be(ContentOf(b));
    }
}
