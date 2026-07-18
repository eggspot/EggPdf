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
}
