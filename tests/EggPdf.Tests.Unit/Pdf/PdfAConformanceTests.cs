using System;
using System.Collections.Generic;
using System.Text;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfAConformanceTests
{
    private static PdfDocument DocWithPage(PdfAConformance? conformance = null)
    {
        var doc = new PdfDocument { Conformance = conformance };
        var page = doc.AddPage(595.28f, 841.89f);
        page.AddText("Hello", 72, 720, "Helvetica", 12);
        return doc;
    }

    [Fact]
    public void NoConformance_OmitsMetadataAndOutputIntents()
    {
        var text = Encoding.Latin1.GetString(DocWithPage().ToByteArray());
        text.Should().NotContain("/OutputIntents");
        text.Should().NotContain("/Metadata");
    }

    [Fact]
    public void PdfA2b_Catalog_ReferencesOutputIntentAndMetadata()
    {
        var text = Encoding.Latin1.GetString(DocWithPage(PdfAConformance.PdfA2b).ToByteArray());
        text.Should().Contain("/OutputIntents [");
        text.Should().Contain("/S /GTS_PDFA1");
        text.Should().Contain("/DestOutputProfile");
        text.Should().Contain("/Type /Metadata");
        text.Should().Contain("/Subtype /XML");
    }

    [Theory]
    [InlineData(PdfAConformance.PdfA2b, "2", "B")]
    [InlineData(PdfAConformance.PdfA3b, "3", "B")]
    [InlineData(PdfAConformance.PdfA2u, "2", "U")]
    [InlineData(PdfAConformance.PdfA3u, "3", "U")]
    public void XmpMetadata_DeclaresRequestedPartAndConformanceLevel(PdfAConformance conformance, string expectedPart, string expectedLevel)
    {
        var text = Encoding.Latin1.GetString(DocWithPage(conformance).ToByteArray());
        text.Should().Contain($"<pdfaid:part>{expectedPart}</pdfaid:part>");
        text.Should().Contain($"<pdfaid:conformance>{expectedLevel}</pdfaid:conformance>");
    }

    [Fact]
    public void Conformance_WithEncryption_ThrowsBeforeWriting()
    {
        var doc = new PdfDocument
        {
            Conformance = PdfAConformance.PdfA2b,
            Encryption = new PdfEncryption(),
        };
        doc.AddPage(595.28f, 841.89f);

        Action act = () => doc.ToByteArray();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Conformance_Trailer_HasFileId()
    {
        var text = Encoding.Latin1.GetString(DocWithPage(PdfAConformance.PdfA2b).ToByteArray());
        text.Should().MatchRegex(@"/ID \[<[0-9A-Fa-f]{32}> <[0-9A-Fa-f]{32}>\]");
    }

    [Fact]
    public void IccProfile_HasValidHeaderAndRequiredTags()
    {
        var icc = IccSrgbProfile.Generate();

        icc.Length.Should().BeGreaterThan(128);
        Encoding.ASCII.GetString(icc, 12, 4).Should().Be("mntr");
        Encoding.ASCII.GetString(icc, 16, 4).Should().Be("RGB ");
        Encoding.ASCII.GetString(icc, 20, 4).Should().Be("XYZ ");
        Encoding.ASCII.GetString(icc, 36, 4).Should().Be("acsp");

        uint tagCount = ReadU32(icc, 128);
        tagCount.Should().BeGreaterThan(0);

        var sigs = new HashSet<string>();
        for (int i = 0; i < tagCount; i++)
        {
            int entryOffset = 132 + i * 12;
            sigs.Add(Encoding.ASCII.GetString(icc, entryOffset, 4));
            uint tagOffset = ReadU32(icc, entryOffset + 4);
            uint tagSize = ReadU32(icc, entryOffset + 8);
            (tagOffset + tagSize).Should().BeLessOrEqualTo((uint)icc.Length);
        }

        foreach (var required in new[] { "desc", "cprt", "wtpt", "rXYZ", "gXYZ", "bXYZ", "rTRC", "gTRC", "bTRC" })
            sigs.Should().Contain(required);
    }

    private static uint ReadU32(byte[] buf, int offset)
        => ((uint)buf[offset] << 24) | ((uint)buf[offset + 1] << 16) | ((uint)buf[offset + 2] << 8) | buf[offset + 3];
}
