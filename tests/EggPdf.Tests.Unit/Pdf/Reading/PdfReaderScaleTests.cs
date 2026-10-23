using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf.Reading;

[CollectionDefinition(Name, DisableParallelization = true)]
public class MemoryMeasurementCollection
{
    public const string Name = "Memory measurement";
}

[Collection(MemoryMeasurementCollection.Name)]
public class PdfReaderScaleTests
{
    private const int PageTotal = 150_000;
    private const int PagesPerLeafNode = 100;
    private const int LeafNodesPerBranch = 100;
    private const int FirstBranchNumber = 3;
    private const long Megabyte = 1024 * 1024;

    private static readonly Encoding Latin1 = Encoding.GetEncoding("ISO-8859-1");

    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static int PageWidth(int pageNumber) => 200 + pageNumber % 1000;

    private static string PageBody(int pageNumber, int parent) =>
        $"<< /Type /Page /Parent {N(parent)} 0 R /MediaBox [0 0 {N(PageWidth(pageNumber))} 800] /Resources << /Font << /F1 4 0 R >> >> >>";

    private static TestPdfBuilder BalancedTree()
    {
        int leafNodes = PageTotal / PagesPerLeafNode;
        int branches = leafNodes / LeafNodesPerBranch;
        int firstLeafNode = FirstBranchNumber + branches;
        int firstPage = firstLeafNode + leafNodes;

        var builder = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var rootKids = new StringBuilder();
        for (int b = 0; b < branches; b++)
        {
            int branch = FirstBranchNumber + b;
            rootKids.Append(N(branch)).Append(" 0 R ");
            var branchKids = new StringBuilder();
            for (int l = 0; l < LeafNodesPerBranch; l++)
            {
                int leafIndex = b * LeafNodesPerBranch + l;
                int leafNode = firstLeafNode + leafIndex;
                branchKids.Append(N(leafNode)).Append(" 0 R ");
                var pageKids = new StringBuilder();
                for (int p = 0; p < PagesPerLeafNode; p++)
                {
                    int pageIndex = leafIndex * PagesPerLeafNode + p;
                    int pageNumber = firstPage + pageIndex;
                    pageKids.Append(N(pageNumber)).Append(" 0 R ");
                    builder.Add(pageNumber, PageBody(pageIndex + 1, leafNode));
                }
                builder.Add(leafNode, $"<< /Type /Pages /Parent {N(branch)} 0 R /Kids [{pageKids}] /Count {N(PagesPerLeafNode)} >>");
            }
            builder.Add(branch, $"<< /Type /Pages /Parent 2 0 R /Kids [{branchKids}] /Count {N(PagesPerLeafNode * LeafNodesPerBranch)} >>");
        }
        builder.Add(2, $"<< /Type /Pages /Kids [{rootKids}] /Count {N(PageTotal)} >>");
        return builder;
    }

    private static byte[] FlatTreeInOneObjectStream() => FlatTreeInOneObjectStream(out _);

    private static byte[] FlatTreeInOneObjectStream(out int decodedObjectStreamLength)
    {
        var header = new StringBuilder();
        var body = new StringBuilder();
        var kids = new StringBuilder();
        for (int i = 0; i < PageTotal; i++)
        {
            int number = 10 + i;
            header.Append(N(number)).Append(' ').Append(N(body.Length)).Append(' ');
            body.Append(PageBody(i + 1, 2)).Append('\n');
            kids.Append(N(number)).Append(" 0 R ");
        }
        decodedObjectStreamLength = header.Length + body.Length;
        var compressed = TestPdfBuilder.Zlib(Latin1.GetBytes(header.ToString() + body));
        var head = Latin1.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n" +
                                   "2 0 obj << /Type /Pages /Kids [" + kids + "] /Count " + N(PageTotal) + " >> endobj\n" +
                                   "4 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj\n" +
                                   "3 0 obj << /Type /ObjStm /N " + N(PageTotal) + " /First " + N(header.Length) +
                                   " /Filter /FlateDecode /Length " + N(compressed.Length) + " >>\nstream\n");
        var tail = Latin1.GetBytes("\nendstream\nendobj\n");
        var pdf = new byte[head.Length + compressed.Length + tail.Length];
        head.CopyTo(pdf, 0);
        compressed.CopyTo(pdf, head.Length);
        tail.CopyTo(pdf, head.Length + compressed.Length);
        return pdf;
    }

    private static long AllocatedWhile(Action action)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void Open_150kPagesBalancedTreeClassicXref_ReadsAnyPageWithLittleMemory()
    {
        var pdf = BalancedTree().BuildClassic(root: 1);
        PdfReader? reader = null;

        long allocated = AllocatedWhile(() =>
        {
            reader = PdfReader.Open(pdf);
            reader.PageCount.Should().Be(PageTotal);
            reader.GetPage(1).Width.Should().Be(PageWidth(1));
            reader.GetPage(75_000).Width.Should().Be(PageWidth(75_000));
            reader.GetPage(PageTotal).Width.Should().Be(PageWidth(PageTotal));
        });

        allocated.Should().BeLessThan(4 * Megabyte);
    }

    [Fact]
    public void Open_150kPagesBalancedTreeXrefStream_ReadsAnyPageWithLittleMemory()
    {
        var pdf = BalancedTree().BuildWithXrefStream(root: 1, pngUpPredictor: true);
        PdfReader? reader = null;

        long allocated = AllocatedWhile(() =>
        {
            reader = PdfReader.Open(pdf);
            reader.PageCount.Should().Be(PageTotal);
            reader.GetPage(1).Width.Should().Be(PageWidth(1));
            reader.GetPage(PageTotal).Width.Should().Be(PageWidth(PageTotal));
        });

        allocated.Should().BeLessThan(6 * Megabyte);
    }

    [Fact]
    public void GetPageCount_150kPagesFlatTreeInAnObjectStream_AllocatesLittleBeyondInflatingThatStream()
    {
        var pdf = FlatTreeInOneObjectStream(out int decodedObjectStreamLength);

        long allocated = AllocatedWhile(() => PdfReader.GetPageCount(pdf).Should().Be(PageTotal));

        allocated.Should().BeLessThan(2L * decodedObjectStreamLength + 32 * Megabyte);
    }

    [Fact]
    public void GetPageCount_150kPagesFlatTreeClassicXref_AllocatesLittle()
    {
        var builder = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var kids = new StringBuilder();
        for (int i = 0; i < PageTotal; i++)
        {
            builder.Add(10 + i, PageBody(i + 1, 2));
            kids.Append(N(10 + i)).Append(" 0 R ");
        }
        var pdf = builder.Add(2, $"<< /Type /Pages /Kids [{kids}] /Count {N(PageTotal)} >>").BuildClassic(root: 1);
        PdfReader? reader = null;

        long allocated = AllocatedWhile(() =>
        {
            reader = PdfReader.Open(pdf);
            reader.PageCount.Should().Be(PageTotal);
            reader.GetPage(PageTotal).Width.Should().Be(PageWidth(PageTotal));
        });

        allocated.Should().BeLessThan(16 * Megabyte);
    }

    [Fact]
    public void GetPage_Every150kPageOfAFlatTree_KeepsMemoryBoundedAndTakesLinearTime()
    {
        var pdf = FlatTreeInOneObjectStream();
        long before = GC.GetTotalMemory(forceFullCollection: true);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var reader = PdfReader.Open(pdf);
        int wrong = 0;
        for (int page = 1; page <= reader.PageCount; page++)
            if (reader.GetPage(page).Width != PageWidth(page)) wrong++;

        watch.Stop();
        long retained = GC.GetTotalMemory(forceFullCollection: true) - before;
        GC.KeepAlive(reader);
        wrong.Should().Be(0);
        retained.Should().BeLessThan(48 * Megabyte);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void GetPage_Every150kPageOfABalancedTree_KeepsMemoryBounded()
    {
        var pdf = BalancedTree().BuildClassic(root: 1);
        long before = GC.GetTotalMemory(forceFullCollection: true);

        var reader = PdfReader.Open(pdf);
        int wrong = 0;
        for (int page = 1; page <= reader.PageCount; page++)
            if (reader.GetPage(page).Width != PageWidth(page)) wrong++;

        long retained = GC.GetTotalMemory(forceFullCollection: true) - before;
        GC.KeepAlive(reader);
        wrong.Should().Be(0);
        retained.Should().BeLessThan(32 * Megabyte);
    }

    [Fact]
    public void Open_ClassicXrefWithNineteenByteRows_ReadsObjects()
    {
        var original = Latin1.GetString(BalancedTree().BuildClassic(root: 1));
        int xref = original.LastIndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        var rows = original.Substring(xref).Replace(" n \n", " n\n").Replace(" f \n", " f\n");
        var pdf = Latin1.GetBytes(original.Substring(0, xref) + rows);

        var reader = PdfReader.Open(pdf);

        reader.PageCount.Should().Be(PageTotal);
        reader.GetPage(PageTotal).Width.Should().Be(PageWidth(PageTotal));
    }

    [Fact]
    public void Open_ClassicXrefWithUnpaddedRows_ReadsObjects()
    {
        var original = Latin1.GetString(new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 400] >>")
            .Add(4, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 500 600] >>")
            .BuildClassic(root: 1));
        int xref = original.LastIndexOf("\nxref\n", StringComparison.Ordinal) + 1;
        var rows = Regex.Replace(original.Substring(xref), @"(\d{10}) (\d{5}) ([nf]) \n",
            m => long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + " " +
                 int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + " " +
                 m.Groups[3].Value + "\r\n");
        var pdf = Latin1.GetBytes(original.Substring(0, xref) + rows);

        var reader = PdfReader.Open(pdf);

        reader.PageCount.Should().Be(2);
        reader.GetPage(2).Width.Should().Be(500);
    }

    [Fact]
    public void GetPage_MixedTree_NumbersPagesByTheCountEntries()
    {
        var pdf = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R 4 0 R 7 0 R 8 0 R] /Count 4 >>")
            .Add(3, "<< /Type /Pages /Parent 2 0 R /Kids [5 0 R 6 0 R] /Count 2 >>")
            .Add(4, "<< /Type /Pages /Parent 2 0 R /Kids [] /Count 0 >>")
            .Add(5, "<< /Type /Page /Parent 3 0 R /MediaBox [0 0 101 800] >>")
            .Add(6, "<< /Type /Page /Parent 3 0 R /MediaBox [0 0 102 800] >>")
            .Add(7, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 103 800] >>")
            .Add(8, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 104 800] >>")
            .BuildClassic(root: 1);

        var reader = PdfReader.Open(pdf);

        reader.PageCount.Should().Be(4);
        for (int page = 1; page <= 4; page++) reader.GetPage(page).Width.Should().Be(100 + page);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/Count 0")]
    [InlineData("/Count -3")]
    [InlineData("/Count (two)")]
    public void Open_RootCountMissingOrInvalid_CountsThePagesFromTheKids(string count)
    {
        var pdf = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, $"<< /Type /Pages /Kids [3 0 R 4 0 R] {count} >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 101 800] >>")
            .Add(4, "<< /Type /Pages /Parent 2 0 R /Kids [5 0 R 6 0 R] >>")
            .Add(5, "<< /Type /Page /Parent 4 0 R /MediaBox [0 0 102 800] >>")
            .Add(6, "<< /Type /Page /Parent 4 0 R /MediaBox [0 0 103 800] >>")
            .BuildClassic(root: 1);

        var reader = PdfReader.Open(pdf);

        reader.PageCount.Should().Be(3);
        reader.GetPage(3).Width.Should().Be(103);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(1)]
    public void Open_RootCountDisagreesWithThePages_CountsThePagesInstead(int declared)
    {
        var pdf = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, $"<< /Type /Pages /Kids [3 0 R 4 0 R] /Count {N(declared)} >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 101 800] >>")
            .Add(4, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 102 800] >>")
            .BuildClassic(root: 1);

        var reader = PdfReader.Open(pdf);

        reader.PageCount.Should().Be(2);
        reader.GetPage(2).Width.Should().Be(102);
    }

    [Fact]
    public void GetPage_InnerCountDisagreesOnTheWayToThePage_ThrowsInsteadOfGuessing()
    {
        var pdf = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R 6 0 R] /Count 3 >>")
            .Add(3, "<< /Type /Pages /Parent 2 0 R /Kids [4 0 R 5 0 R 7 0 R] /Count 2 >>")
            .Add(4, "<< /Type /Page /Parent 3 0 R /MediaBox [0 0 101 800] >>")
            .Add(5, "<< /Type /Page /Parent 3 0 R /MediaBox [0 0 102 800] >>")
            .Add(6, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 103 800] >>")
            .Add(7, "<< /Type /Page /Parent 3 0 R /MediaBox [0 0 104 800] >>")
            .BuildClassic(root: 1);
        var reader = PdfReader.Open(pdf);

        Action first = () => reader.GetPage(1);

        reader.PageCount.Should().Be(3);
        reader.GetPage(3).Width.Should().Be(103);
        first.Should().Throw<PdfFormatException>().WithMessage("*inconsistent*");
    }

    [Fact]
    public void Open_RootCountAboveTheObjectCount_CountsThePagesInstead()
    {
        var pdf = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 900000 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>")
            .BuildClassic(root: 1);

        PdfReader.Open(pdf).PageCount.Should().Be(1);
    }

    [Fact]
    public void GetPage_PageInheritsAttributesAlongTheWholePath()
    {
        var pdf = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 600 800] /Rotate 90 >>")
            .Add(3, "<< /Type /Pages /Parent 2 0 R /Kids [4 0 R] /Count 1 /CropBox [0 0 300 400] >>")
            .Add(4, "<< /Type /Page /Parent 3 0 R >>")
            .BuildClassic(root: 1);

        var page = PdfReader.Open(pdf).GetPage(1);

        page.Width.Should().Be(400);
        page.Height.Should().Be(300);
        page.Rotation.Should().Be(90);
    }
}
