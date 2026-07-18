using System;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>
/// PdfMerger must handle inputs produced with the production default of
/// FlateDecode-compressed content streams (it extracts and re-emits page
/// operators), and must refuse encrypted inputs instead of silently
/// producing ciphertext pages.
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

    [Fact]
    public void Merge_UncompressedInputs_KeepsBothPagesText()
    {
        var merger = new PdfMerger();
        merger.Add(MakePdf("first document", compress: false));
        merger.Add(MakePdf("second document", compress: false));
        var merged = merger.Build();

        var text = System.Text.Encoding.Latin1.GetString(merged);
        text.Should().Contain("(first document) Tj");
        text.Should().Contain("(second document) Tj");
    }

    [Fact]
    public void Merge_CompressedInputs_InflatesAndKeepsBothPagesText()
    {
        // Production renders compress content streams by default — the merger
        // must inflate them, not copy ciphertext as if it were operators.
        var merger = new PdfMerger();
        merger.Add(MakePdf("first compressed", compress: true));
        merger.Add(MakePdf("second compressed", compress: true));
        var merged = merger.Build();

        // The test assembly writes merged output uncompressed (TestSetup),
        // so recovered operators are directly visible.
        var text = System.Text.Encoding.Latin1.GetString(merged);
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
        // A stream whose dict contains a nested sub-dictionary before the
        // /FlateDecode token must still be detected as compressed.
        var doc = new PdfDocument { CompressContentStreams = true };
        var page = doc.AddPage(595, 842);
        page.AddText("nested dict content", 50, 700, "Helvetica", 12);
        var bytes = doc.ToByteArray();

        // Inject a /DecodeParms sub-dict AFTER /FlateDecode, so the nearest
        // "<<" before the stream is the sub-dict's — the exact shape that
        // defeats a LastIndexOf("<<") scan (the token is now outside its
        // window). Anchoring on the object start must still find it.
        var text = System.Text.Encoding.Latin1.GetString(bytes);
        text = ReplaceFirst(text, "/Filter /FlateDecode",
            "/Filter /FlateDecode /DecodeParms << /Predictor 1 >>");
        var mutated = System.Text.Encoding.Latin1.GetBytes(text);

        var merger = new PdfMerger();
        merger.Add(mutated);
        merger.Add(MakePdf("second page", compress: false));

        System.Text.Encoding.Latin1.GetString(merger.Build())
            .Should().Contain("(nested dict content) Tj",
                "the /FlateDecode after a sub-dict must still trigger inflation");
    }

    private static string ReplaceFirst(string haystack, string find, string replace)
    {
        int i = haystack.IndexOf(find, StringComparison.Ordinal);
        return i < 0 ? haystack : haystack.Substring(0, i) + replace + haystack.Substring(i + find.Length);
    }
}
