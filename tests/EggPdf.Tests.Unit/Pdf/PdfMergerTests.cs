using System;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>
/// PdfMerger must resolve each page dictionary to the content stream it
/// actually references (font programs and images are written before page
/// content, so file order is meaningless), handle inputs produced with the
/// production default of FlateDecode-compressed content streams, and refuse
/// encrypted inputs instead of silently producing ciphertext pages.
/// </summary>
public class PdfMergerTests
{
    private static byte[] MakePdf(string text, bool compress, PdfEncryption? encryption = null)
    {
        var doc = new PdfDocument { CompressContentStreams = compress, Encryption = encryption };
        var page = doc.AddPage(595, 842);
        page.AddText(text, 50, 700, "Helvetica", 12);
        return doc.ToByteArray();
    }

    private static string Latin1(byte[] pdf) => System.Text.Encoding.Latin1.GetString(pdf);

    [Fact]
    public void Merge_UncompressedInputs_KeepsBothPagesText()
    {
        var merger = new PdfMerger();
        merger.Add(MakePdf("first document", compress: false));
        merger.Add(MakePdf("second document", compress: false));

        var text = Latin1(merger.Build());
        text.Should().Contain("(first document) Tj");
        text.Should().Contain("(second document) Tj");
    }

    [Fact]
    public void Merge_CompressedInputs_InflatesAndKeepsBothPagesText()
    {
        // Production renders compress content streams by default — the merger
        // must inflate them, not copy compressed bytes as if they were operators.
        var merger = new PdfMerger();
        merger.Add(MakePdf("first compressed", compress: true));
        merger.Add(MakePdf("second compressed", compress: true));

        var text = Latin1(merger.Build());
        text.Should().Contain("(first compressed) Tj");
        text.Should().Contain("(second compressed) Tj");
    }

    [Fact]
    public void Merge_EncryptedInput_ThrowsInsteadOfProducingGarbage()
    {
        var merger = new PdfMerger();
        merger.Add(MakePdf("secret", compress: false,
            encryption: new PdfEncryption { OwnerPassword = "owner" }));
        merger.Add(MakePdf("plain", compress: false));

        var act = () => merger.Build();
        act.Should().Throw<NotSupportedException>(
            "merging encrypted PDFs would silently re-emit ciphertext as page operators");
    }

    [Fact]
    public void Merge_ForeignPdf_IndirectEncryptReference_IsRefused()
    {
        // Most non-EggPdf writers reference the encryption dict indirectly
        // ("/Encrypt 9 0 R") rather than inline — the guard must catch it.
        var foreign = System.Text.Encoding.ASCII.GetBytes(
            "%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\n" +
            "trailer\n<< /Root 1 0 R /Encrypt 9 0 R /ID [<00> <00>] >>\n%%EOF");
        var merger = new PdfMerger();
        merger.Add(foreign);
        merger.Add(MakePdf("plain", compress: false));

        var act = () => merger.Build();
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Merge_StreamDictWithSubDictionary_StillInflates()
    {
        var doc = new PdfDocument { CompressContentStreams = true };
        var page = doc.AddPage(595, 842);
        page.AddText("nested dict content", 50, 700, "Helvetica", 12);

        // Inject a /DecodeParms sub-dict AFTER /FlateDecode, so the nearest
        // "<<" before the stream is the sub-dict's — the shape that defeats a
        // LastIndexOf("<<") scan. Anchoring on the object start still finds it.
        var text = ReplaceFirst(Latin1(doc.ToByteArray()), "/Filter /FlateDecode",
            "/Filter /FlateDecode /DecodeParms << /Predictor 1 >>");
        var mutated = System.Text.Encoding.Latin1.GetBytes(text);

        var merger = new PdfMerger();
        merger.Add(mutated);
        merger.Add(MakePdf("second page", compress: false));

        Latin1(merger.Build()).Should().Contain("(nested dict content) Tj",
            "a /FlateDecode before a sub-dict must still trigger inflation");
    }

    [Fact]
    public void Merge_RealRendersWithEmbeddedFonts_RecoversPageContentNotFontData()
    {
        // Real renders write font programs and ToUnicode CMaps BEFORE page
        // content streams, so selecting the Nth stream in file order yields
        // font data instead of page operators. The merger must follow each
        // page's /Contents reference.
        var first = HtmlToPdf.Render("<html><body><p>Giấy Chứng Nhận số một</p></body></html>");
        var second = HtmlToPdf.Render("<html><body><p>Trang thứ hai</p></body></html>");

        Latin1(first).Should().Contain("/FontFile2",
            "the input must embed a font, which is what shifts stream order");

        var text = Latin1(new PdfMerger().Add(first).Add(second).Build());

        // Vietnamese renders through CID fonts as glyph IDs, so assert on the
        // recovered operators rather than the literal characters.
        text.Should().Contain("BT", "merged pages must carry real text operators");
        text.Should().Contain("Tf", "a font must be selected in the merged content");
        text.Should().Contain("Tj", "glyphs must be shown");
    }

    [Fact]
    public void Merge_PagesOfDifferentSizes_KeepsEachPageOwnMediaBox()
    {
        // Page size was previously read once from the first /MediaBox in the
        // document and applied to every page of it — so the sizes must differ
        // WITHIN a single input document for this to pin the bug.
        var mixed = new PdfDocument();
        mixed.AddPage(595.28f, 841.89f).AddText("a4 page", 50, 700, "Helvetica", 12);
        mixed.AddPage(612f, 792f).AddText("letter page", 50, 700, "Helvetica", 12);

        var text = Latin1(new PdfMerger().Add(mixed.ToByteArray()).Add(MakePdf("tail", false)).Build());

        text.Should().Contain("/MediaBox [0 0 595.28 841.89]");
        text.Should().Contain("/MediaBox [0 0 612.00 792.00]");
    }

    [Fact]
    public void Merge_MultiPageDocument_KeepsEveryPage()
    {
        var multi = new PdfDocument();
        multi.AddPage(595, 842).AddText("page one", 50, 700, "Helvetica", 12);
        multi.AddPage(595, 842).AddText("page two", 50, 700, "Helvetica", 12);
        multi.AddPage(595, 842).AddText("page three", 50, 700, "Helvetica", 12);

        var text = Latin1(new PdfMerger().Add(multi.ToByteArray()).Add(MakePdf("tail", false)).Build());

        text.Should().Contain("(page one) Tj");
        text.Should().Contain("(page two) Tj");
        text.Should().Contain("(page three) Tj");
        text.Should().Contain("(tail) Tj");
    }

    private static string ReplaceFirst(string haystack, string find, string replace)
    {
        int i = haystack.IndexOf(find, StringComparison.Ordinal);
        return i < 0 ? haystack : haystack.Substring(0, i) + replace + haystack.Substring(i + find.Length);
    }
}
