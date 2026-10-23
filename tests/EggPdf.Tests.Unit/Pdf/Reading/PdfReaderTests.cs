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
    public void Open_MacOsQuartzOutput_ReadsPagesAndLetterSize()
    {
        var pdf = System.IO.File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, "Pdf", "Reading", "Fixtures", "macos-quartz-2-pages.pdf"));

        var reader = PdfReader.Open(pdf);

        reader.PageCount.Should().Be(2);
        reader.GetPage(2).Width.Should().Be(612);
        reader.GetPage(2).Height.Should().Be(792);
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

        act.Should().Throw<PdfFormatException>().WithMessage("*cycle*");
    }

    private static byte[] Latin1Bytes(string text) => Encoding.GetEncoding("ISO-8859-1").GetBytes(text);

    private static void ShouldFinishQuickly(Action open)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try { open(); }
        catch (PdfFormatException) { }
        catch (NotSupportedException) { }
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Open_LongChainOfIndirectStreamLengths_DoesNotOverflowTheStack()
    {
        var builder = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        for (int k = 3; k < 3 + 20_000; k++)
            builder.Add(k, "<< /Length " + (k + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 R >>\nstream\nx\nendstream");

        ShouldFinishQuickly(() => PdfReader.Open(builder.BuildClassic(root: 1)));
    }

    [Fact]
    public void Open_RepairOfManyUnterminatedStreams_TakesLinearTime()
    {
        var text = new StringBuilder("%PDF-1.7\n");
        for (int k = 1; k <= 80_000; k++)
            text.Append(k.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" 0 obj<<>>stream\n");

        ShouldFinishQuickly(() => PdfReader.Open(Latin1Bytes(text.ToString())));
    }

    [Fact]
    public void Open_ObjectStreamWithRepeatedOffsets_TakesLinearTime()
    {
        const int count = 32_000;
        var header = new StringBuilder();
        for (int i = 0; i < count; i++)
            header.Append((100 + i).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" 0 ");
        var body = "[" + string.Join(" ", new string('0', count).ToCharArray()) + "]";
        var data = header + body;
        var text = "%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n" +
                   "2 0 obj << /Type /Pages /Kids [] /Count 0 >> endobj\n" +
                   "3 0 obj << /Type /ObjStm /N " + count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                   " /First " + header.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                   " /Length " + data.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " >>\nstream\n" + data + "\nendstream\nendobj\n";

        ShouldFinishQuickly(() => PdfReader.Open(Latin1Bytes(text)));
    }

    [Fact]
    public void Open_RepairOfManyUnterminatedStrings_TakesLinearTime()
    {
        var text = new StringBuilder("%PDF-1.7\n");
        for (int k = 1; k <= 40_000; k++)
            text.Append(k.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" 0 obj<</Type/Catalog/A(\n");

        ShouldFinishQuickly(() => PdfReader.Open(Latin1Bytes(text.ToString())));
    }

    [Fact]
    public void Open_RepairOfManyObjectStreamsSharingOneEndstream_TakesLinearTime()
    {
        var text = new StringBuilder("%PDF-1.7\n");
        for (int k = 1; k <= 200_000; k++)
            text.Append(k.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" 0 obj<</Type/ObjStm/N 1/First 0>>stream\n");
        text.Append("endstream\n");

        ShouldFinishQuickly(() => PdfReader.Open(Latin1Bytes(text.ToString())));
    }

    private static void ShouldStayWithinMemory(byte[] pdf)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        ShouldFinishQuickly(() => PdfReader.Open(pdf));
        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(400L * 1024 * 1024,
            "the materialized budget (16x input + 256 MB, strings growing by doubling) bounds it; unbounded, these inputs allocate several GB");
    }

    [Fact]
    public void Open_RepairOfManyUnterminatedTrailers_TakesLinearTime()
    {
        var text = new StringBuilder("%PDF-1.7\n");
        for (int k = 0; k < 80_000; k++) text.Append("trailer(");

        ShouldFinishQuickly(() => PdfReader.Open(Latin1Bytes(text.ToString())));
    }

    [Fact]
    public void Open_150kPagesInCompressedObjectStream_IsReadInFull()
    {
        const int count = 150_000;
        var header = new StringBuilder();
        var body = new StringBuilder();
        var kids = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            int number = 10 + i;
            header.Append(number.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(' ')
                  .Append(body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
            body.Append("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> /ProcSet [/PDF /Text] >> /Contents 5 0 R >>\n");
            kids.Append(number.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" 0 R ");
        }
        var compressed = TestPdfBuilder.Zlib(Latin1Bytes(header.ToString() + body));
        var head = Latin1Bytes("%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n" +
                               "2 0 obj << /Type /Pages /Kids [" + kids + "] /Count " + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " >> endobj\n" +
                               "4 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj\n" +
                               "5 0 obj << /Length 0 >>\nstream\n\nendstream\nendobj\n" +
                               "3 0 obj << /Type /ObjStm /N " + count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                               " /First " + header.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                               " /Filter /FlateDecode /Length " + compressed.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " >>\nstream\n");
        var tail = Latin1Bytes("\nendstream\nendobj\n");
        var pdf = new byte[head.Length + compressed.Length + tail.Length];
        head.CopyTo(pdf, 0);
        compressed.CopyTo(pdf, head.Length);
        tail.CopyTo(pdf, head.Length + compressed.Length);

        PdfReader.Open(pdf).PageCount.Should().Be(count);
    }

    [Fact]
    public void Open_ObjectStreamDecodingToAHugeArray_StaysWithinMemory()
    {
        var data = new StringBuilder("100 0 [");
        for (int i = 0; i < 15_000_000; i++) data.Append("<<>>");
        data.Append(']');
        var compressed = TestPdfBuilder.Zlib(Latin1Bytes(data.ToString()));
        var head = Latin1Bytes("%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 100 0 R >> endobj\n" +
                               "3 0 obj << /Type /ObjStm /N 1 /First 6 /Filter /FlateDecode /Length " +
                               compressed.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " >>\nstream\n");
        var tail = Latin1Bytes("\nendstream\nendobj\n");
        var pdf = new byte[head.Length + compressed.Length + tail.Length];
        head.CopyTo(pdf, 0);
        compressed.CopyTo(pdf, head.Length);
        tail.CopyTo(pdf, head.Length + compressed.Length);

        long before = GC.GetAllocatedBytesForCurrentThread();
        ShouldFinishQuickly(() => PdfReader.Open(pdf));

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(500L * 1024 * 1024,
            "decoding the 60 MB stream is allowed, materializing 15 million objects from a tiny file isn't");
    }

    [Fact]
    public void Open_OverlappingStringsAcrossObjects_StaysWithinMemory()
    {
        const int count = 12_000;
        var builder = new TestPdfBuilder().Add(1, "<< /Type /Catalog /Pages 2 0 R >>");
        var kids = new StringBuilder();
        for (int k = 3; k < 3 + count; k++)
        {
            kids.Append(k.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" 0 R ");
            builder.Add(k, k == 2 + count ? "(" + new string(')', count) : "(");
        }
        builder.Add(2, "<< /Type /Pages /Kids [" + kids + "] /Count " + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " >>");

        ShouldStayWithinMemory(builder.BuildClassic(root: 1));
    }

    [Fact]
    public void Open_OverlappingStringsInObjectStream_StaysWithinMemory()
    {
        const int count = 32_000;
        var header = new StringBuilder();
        var kids = new StringBuilder();
        for (int i = 0; i < count; i++)
        {
            header.Append((100 + i).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(' ')
                  .Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
            kids.Append((100 + i).ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" 0 R ");
        }
        var compressed = TestPdfBuilder.Zlib(Latin1Bytes(header + new string('(', count) + new string(')', count)));
        var head = Latin1Bytes("%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n" +
                               "2 0 obj << /Type /Pages /Kids [" + kids + "] /Count " + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " >> endobj\n" +
                               "3 0 obj << /Type /ObjStm /N " + count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                               " /First " + header.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                               " /Filter /FlateDecode /Length " + compressed.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " >>\nstream\n");
        var tail = Latin1Bytes("\nendstream\nendobj\n");
        var pdf = new byte[head.Length + compressed.Length + tail.Length];
        head.CopyTo(pdf, 0);
        compressed.CopyTo(pdf, head.Length);
        tail.CopyTo(pdf, head.Length + compressed.Length);

        ShouldStayWithinMemory(pdf);
    }

    [Fact]
    public void Open_TinyXrefStreamDeclaringMillionsOfEntries_StaysWithinMemory()
    {
        var rows = TestPdfBuilder.Zlib(new byte[8 * 1024 * 1024]);
        var latin1 = Encoding.GetEncoding("ISO-8859-1");
        var head = latin1.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n2 0 obj << /Type /Pages /Kids [] /Count 0 >> endobj\n");
        var streamHead = latin1.GetBytes("3 0 obj << /Type /XRef /W [1 0 0] /Index [0 8388607] /Size 8388607 /Root 1 0 R /Filter /FlateDecode /Length "
            + rows.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " >>\nstream\n");
        var tail = latin1.GetBytes("\nendstream\nendobj\nstartxref\n" + head.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n%%EOF\n");
        var pdf = new byte[head.Length + streamHead.Length + rows.Length + tail.Length];
        head.CopyTo(pdf, 0);
        streamHead.CopyTo(pdf, head.Length);
        rows.CopyTo(pdf, head.Length + streamHead.Length);
        tail.CopyTo(pdf, head.Length + streamHead.Length + rows.Length);

        long before = GC.GetAllocatedBytesForCurrentThread();
        ShouldFinishQuickly(() => PdfReader.Open(pdf));

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(200L * 1024 * 1024);
    }

    [Fact]
    public void Open_ObjectDeletedInLaterRevision_IsNotResurrected()
    {
        var original = TwoPageDocument().BuildClassic(root: 1);
        var text = Encoding.GetEncoding("ISO-8859-1").GetString(original);
        int startxref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        var previous = text.Substring(startxref + 9).Trim().Split('\n')[0].Trim();
        var update = new StringBuilder(text);
        int pagesOffset = update.Length;
        update.Append("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] >>\nendobj\n");
        int xref = update.Length;
        update.Append("xref\n2 1\n").Append(pagesOffset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n")
              .Append("4 1\n0000000000 00001 f \n")
              .Append("trailer\n<< /Size 5 /Root 1 0 R /Prev ").Append(previous).Append(" >>\nstartxref\n")
              .Append(xref.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\n%%EOF\n");

        var reader = PdfReader.Open(Latin1Bytes(update.ToString()));

        reader.Store.Get(4).Should().BeOfType<PdfNull>("object 4 was freed in the newest revision");
        reader.PageCount.Should().Be(1);
    }

    [Fact]
    public void IsEncrypted_EncryptNull_IsFalse()
    {
        var pdf = TwoPageDocument().BuildClassic(root: 1, extraTrailer: "/Encrypt null");

        PdfReader.Open(pdf).IsEncrypted.Should().BeFalse();
    }

    [Fact]
    public void Open_PagesNodeWithoutKids_IsNotCountedAsAPage()
    {
        var pdf = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Pages /Count 0 >>")
            .Add(4, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>")
            .BuildClassic(root: 1);

        PdfReader.Open(pdf).PageCount.Should().Be(1);
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
        var withBomb = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", inObjectStream: true)
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 10 10] >>")
            .BuildWithXrefStream(root: 1, pngUpPredictor: false, objectStreamData: bomb);

        Action act = () => PdfReader.Open(withBomb);

        act.Should().Throw<PdfFormatException>().WithMessage("*64 MB*");
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
        Action bytes = () => PdfReader.Open((byte[])null!);
        Action stream = () => PdfReader.Open((System.IO.Stream)null!);
        Action path = () => PdfReader.Open((string)null!);

        bytes.Should().Throw<ArgumentNullException>();
        stream.Should().Throw<ArgumentNullException>();
        path.Should().Throw<ArgumentNullException>();
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
