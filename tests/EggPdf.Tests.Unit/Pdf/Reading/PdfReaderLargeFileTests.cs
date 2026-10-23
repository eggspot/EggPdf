using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf.Reading;

[Collection(MemoryMeasurementCollection.Name)]
public class PdfReaderLargeFileTests
{
    private const long FillerBytes = 5L * 1024 * 1024 * 1024;
    private const long Megabyte = 1024 * 1024;

    private static readonly Encoding Latin1 = Encoding.GetEncoding("ISO-8859-1");

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==");

    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);

    private sealed class VirtualFile : Stream
    {
        private readonly byte[] _head;
        private readonly long _fillerLength;
        private readonly byte[] _tail;
        private long _position;

        public VirtualFile(byte[] head, long fillerLength, byte[] tail)
        {
            _head = head;
            _fillerLength = fillerLength;
            _tail = tail;
        }

        public long TailStart => _head.Length + _fillerLength;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => TailStart + _tail.Length;

        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int written = 0;
            while (written < count && _position < Length)
            {
                if (_position < _head.Length)
                {
                    int take = (int)Math.Min(count - written, _head.Length - _position);
                    Array.Copy(_head, _position, buffer, offset + written, take);
                    _position += take;
                    written += take;
                }
                else if (_position < TailStart)
                {
                    int take = (int)Math.Min(count - written, TailStart - _position);
                    Array.Clear(buffer, offset + written, take);
                    _position += take;
                    written += take;
                }
                else
                {
                    int take = (int)Math.Min(count - written, Length - _position);
                    Array.Copy(_tail, _position - TailStart, buffer, offset + written, take);
                    _position += take;
                    written += take;
                }
            }
            return written;
        }

        public override long Seek(long offset, SeekOrigin origin) => _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => Length + offset,
        };

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class TailCapture : Stream
    {
        private readonly long _keepFrom;
        private readonly MemoryStream _kept = new();
        private long _position;

        public TailCapture(long keepFrom) => _keepFrom = keepFrom;

        public byte[] Kept => _kept.ToArray();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _position;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            long end = _position + count;
            if (end > _keepFrom)
            {
                int skip = (int)Math.Max(0, _keepFrom - _position);
                _kept.Write(buffer, offset + skip, count - skip);
            }
            _position = end;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private static VirtualFile HugeDocument(bool crossReferenceStream)
    {
        var head = Latin1.GetBytes("%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n3 0 obj\n<< /Length " +
                                   N(FillerBytes) + " >>\nstream\n");
        long fillerEnd = head.Length + FillerBytes;
        var tail = new StringBuilder("\nendstream\nendobj\n");
        var offsets = new long[6];
        offsets[1] = 9;
        offsets[3] = head.Length - Latin1.GetBytes("3 0 obj\n<< /Length " + N(FillerBytes) + " >>\nstream\n").Length;
        offsets[2] = fillerEnd + tail.Length;
        tail.Append("2 0 obj\n<< /Type /Pages /Kids [4 0 R] /Count 1 >>\nendobj\n");
        offsets[4] = fillerEnd + tail.Length;
        tail.Append("4 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>\nendobj\n");
        long crossReference = fillerEnd + tail.Length;
        if (crossReferenceStream)
        {
            offsets[5] = crossReference;
            var rows = new List<byte>();
            for (int i = 0; i < 6; i++)
            {
                rows.Add((byte)(i == 0 ? 0 : 1));
                for (int shift = 32; shift >= 0; shift -= 8) rows.Add((byte)(offsets[i] >> shift));
                rows.Add(0);
            }
            tail.Append("5 0 obj\n<< /Type /XRef /Size 6 /W [1 5 1] /Root 1 0 R /Length " + N(rows.Count) + " >>\nstream\n");
            var text = Latin1.GetBytes(tail.ToString());
            var end = Latin1.GetBytes("\nendstream\nendobj\nstartxref\n" + N(crossReference) + "\n%%EOF\n");
            var bytes = new byte[text.Length + rows.Count + end.Length];
            text.CopyTo(bytes, 0);
            rows.CopyTo(bytes, text.Length);
            end.CopyTo(bytes, text.Length + rows.Count);
            return new VirtualFile(head, FillerBytes, bytes);
        }
        tail.Append("xref\n0 5\n0000000000 65535 f \n");
        for (int i = 1; i < 5; i++) tail.Append(offsets[i].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        tail.Append("trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n").Append(N(crossReference)).Append("\n%%EOF\n");
        return new VirtualFile(head, FillerBytes, Latin1.GetBytes(tail.ToString()));
    }

    private static long AllocatedWhile(Action action)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Open_FiveGigabyteStream_ReadsPagesBeyondTwoGigabytesWithLittleMemory(bool crossReferenceStream)
    {
        var file = HugeDocument(crossReferenceStream);
        PdfPageGeometry? page = null;

        long allocated = AllocatedWhile(() =>
        {
            using var reader = PdfReader.Open(file);
            reader.PageCount.Should().Be(1);
            page = reader.GetPage(1);
        });

        page!.Width.Should().Be(595);
        page.Height.Should().Be(842);
        allocated.Should().BeLessThan(4 * Megabyte);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_FiveGigabyteStream_AppendsAnUpdateWithSixtyFourBitOffsets(bool crossReferenceStream)
    {
        var file = HugeDocument(crossReferenceStream);
        var output = new TailCapture(file.Length);

        long allocated = AllocatedWhile(() => new PdfStamper(file).AddImage(1, Png, 10, 10, 50, 50).Build(output));

        var appended = Latin1.GetString(output.Kept);
        output.Length.Should().BeGreaterThan(file.Length);
        appended.Should().Contain("/EggPdfStamp1");
        if (crossReferenceStream) appended.Should().Contain("/W [1 5 2]");
        else appended.Should().MatchRegex(@"\n\d{10} 00000 n \n");
        long startxref = long.Parse(appended.Substring(appended.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10).Split('\n')[0],
            CultureInfo.InvariantCulture);
        startxref.Should().BeGreaterThan(FillerBytes);
        allocated.Should().BeLessThan(4 * Megabyte);
    }

    [Fact]
    public void Build_StampedFiveGigabyteStream_ReopensWithTheStampedPage()
    {
        var file = HugeDocument(crossReferenceStream: true);
        var output = new TailCapture(file.Length);
        new PdfStamper(file).AddImage(1, Png, 10, 10, 50, 50).Build(output);
        var tail = new MemoryStream();
        file.Position = file.TailStart;
        file.CopyTo(tail);
        tail.Write(output.Kept, 0, output.Kept.Length);
        var head = new byte[file.TailStart - FillerBytes];
        file.Position = 0;
        file.Read(head, 0, head.Length);
        var stamped = new VirtualFile(head, FillerBytes, tail.ToArray());

        using var reader = PdfReader.Open(stamped);

        reader.PageCount.Should().Be(1);
        reader.GetPage(1).Width.Should().Be(595);
    }

    [Fact]
    public void Open_150kPageFileFromAStream_ReadsTheLastPageWithLittleMemory()
    {
        var kids = new StringBuilder();
        var builder = new TestPdfBuilder().Add(1, "<< /Type /Catalog /Pages 2 0 R >>");
        for (int node = 0; node < 1500; node++)
        {
            var pages = new StringBuilder();
            for (int page = 0; page < 100; page++)
            {
                int number = 2000 + node * 100 + page;
                pages.Append(N(number)).Append(" 0 R ");
                builder.Add(number, $"<< /Type /Page /Parent {N(3 + node)} 0 R /MediaBox [0 0 {N(200 + page)} 800] >>");
            }
            builder.Add(3 + node, $"<< /Type /Pages /Parent 2 0 R /Kids [{pages}] /Count 100 >>");
            kids.Append(N(3 + node)).Append(" 0 R ");
        }
        var pdf = builder.Add(2, $"<< /Type /Pages /Kids [{kids}] /Count 150000 >>").BuildClassic(root: 1);
        var stream = new MemoryStream(pdf, writable: false);
        double width = 0;

        long allocated = AllocatedWhile(() =>
        {
            using var reader = PdfReader.Open(stream);
            width = reader.GetPage(150_000).Width;
        });

        width.Should().Be(299);
        allocated.Should().BeLessThan(4 * Megabyte);
    }

    [Fact]
    public void Open_FromStreamAndPath_ReadsTheSameAsBytes()
    {
        var bytes = HtmlToPdf.Render("<p>a</p><p style=\"page-break-before:always\">b</p>");
        var path = Path.Combine(Path.GetTempPath(), "eggpdf-reader-" + Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, bytes);
        try
        {
            using (var fromPath = PdfReader.Open(path))
            using (var fromStream = PdfReader.Open(new MemoryStream(bytes)))
            {
                fromPath.PageCount.Should().Be(2);
                fromStream.PageCount.Should().Be(2);
                fromPath.GetPage(2).Width.Should().Be(PdfReader.Open(bytes).GetPage(2).Width);
            }
            PdfReader.GetPageCount(path).Should().Be(2);
            File.Delete(path);
            File.Exists(path).Should().BeFalse("disposing the reader closes the file");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Build_FromStream_MatchesBuildFromBytes()
    {
        var bytes = HtmlToPdf.Render("<p>x</p>");

        var fromStream = new MemoryStream();
        new PdfStamper(new MemoryStream(bytes)).AddImage(1, Png, 5, 5, 20, 20).Build(fromStream);

        fromStream.ToArray().Should().Equal(new PdfStamper(bytes).AddImage(1, Png, 5, 5, 20, 20).Build());
    }

    private static TestPdfBuilder TwoPages() => new TestPdfBuilder()
        .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
        .Add(2, "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>")
        .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 301 800] >>")
        .Add(4, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 302 800] >>");

    [Fact]
    public void Open_StreamWithManyEndstreamKeywords_IndexesThemInOnePass()
    {
        var text = Latin1.GetString(TwoPages().BuildWithXrefStream(root: 1, pngUpPredictor: false));
        int xrefStream = text.LastIndexOf("/Type /XRef", StringComparison.Ordinal);
        int length = text.IndexOf("/Length ", xrefStream, StringComparison.Ordinal);
        int lengthEnd = text.IndexOf(" >>", length, StringComparison.Ordinal);
        var padding = new StringBuilder();
        for (int i = 0; i < 50_000; i++) padding.Append("%endstream\n");
        var pdf = Latin1.GetBytes(text.Substring(0, length) + text.Substring(lengthEnd) + padding);
        PdfPageGeometry? page = null;

        long allocated = AllocatedWhile(() => page = PdfReader.Open(new MemoryStream(pdf)).GetPage(2));

        page!.Width.Should().Be(302);
        allocated.Should().BeLessThan(32 * Megabyte);
    }

    [Fact]
    public void Open_DamagedStreamWithManyTrailers_RepairsInOnePass()
    {
        var text = Latin1.GetString(TwoPages().BuildClassic(root: 1));
        int startxref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        var padding = new StringBuilder();
        for (int i = 0; i < 20_000; i++) padding.Append("trailer\n");
        var pdf = Latin1.GetBytes(text.Substring(0, startxref) + "startxref\n999999999\n%%EOF\n" + padding);
        PdfPageGeometry? page = null;

        long allocated = AllocatedWhile(() => page = PdfReader.Open(new MemoryStream(pdf)).GetPage(2));

        page!.Width.Should().Be(302);
        allocated.Should().BeLessThan(64 * Megabyte);
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void Get_StreamKeywordAtTheEdgeOfTheFirstWindow_ReadsTheStreamData(int shift)
    {
        const string prefix = "5 0 obj\n<< /Length 5 /Pad (";
        const string suffix = ") >>\nstream";
        int pad = 4096 + shift - prefix.Length - suffix.Length;
        var pdf = TwoPages().Add(5, "<< /Length 5 /Pad (" + new string('x', pad) + ") >>\nstream\nhello\nendstream").BuildClassic(root: 1);

        foreach (var source in new[] { PdfByteSource.Over(new MemoryStream(pdf), ownsStream: false), PdfByteSource.Over(pdf) })
        {
            var stream = PdfObjectStore.Load(source).Get(5) as PdfStream;

            stream.Should().NotBeNull();
            Latin1.GetString(PdfStreamDecoder.Decode(stream!)).Should().Be("hello");
        }
    }

    public static IEnumerable<object[]> TokensCutByTheWindow()
    {
        foreach (int target in new[] { 1, 2, 3 })
            foreach (string ending in new[] { " >>", " /F true >>", " /F null >>", " /F false >>" })
                for (int shift = -12; shift <= 2; shift++)
                    yield return new object[] { target, ending, shift };
    }

    [Theory]
    [MemberData(nameof(TokensCutByTheWindow))]
    public void Open_TokenCutByTheEdgeOfTheFirstWindow_ReadsLikeBytes(int target, string ending, int shift)
    {
        var bodies = new Dictionary<int, string>
        {
            [1] = "<< /Type /Catalog /Pages 2 0 R /Pad (",
            [2] = "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 /Pad (",
            [3] = "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 301 800] /Pad (",
        };
        string header = N(target) + " 0 obj\n";
        int pad = 4096 + shift - header.Length - bodies[target].Length - 1 - ending.Length;
        bodies[target] += new string('x', pad) + ")" + ending;
        var pdf = new TestPdfBuilder()
            .Add(1, bodies.TryGetValue(1, out var catalog) && target == 1 ? catalog : "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, target == 2 ? bodies[2] : "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>")
            .Add(3, target == 3 ? bodies[3] : "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 301 800] >>")
            .Add(4, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 302 800] >>")
            .BuildClassic(root: 1);

        using var reader = PdfReader.Open(new MemoryStream(pdf));

        reader.PageCount.Should().Be(2);
        reader.GetPage(1).Width.Should().Be(301);
        reader.GetPage(2).Width.Should().Be(302);
    }

    [Fact]
    public void Open_DamagedFileWithPageNodesBeforeTheirRoot_ReadsFromBytesAndStream()
    {
        var pdf = Latin1.GetBytes("%PDF-1.7\n" +
                                  "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
                                  "3 0 obj\n<< /Type /Page /Parent 5 0 R /MediaBox [0 0 100 800] >>\nendobj\n" +
                                  "4 0 obj\n<< /Type /Page /Parent 5 0 R /MediaBox [0 0 300 800] >>\nendobj\n" +
                                  "5 0 obj\n<< /Type /Pages /Parent 2 0 R /Kids [3 0 R 4 0 R] /Count 2 >>\nendobj\n" +
                                  "2 0 obj\n<< /Type /Pages /Kids [5 0 R] /Count 2 >>\nendobj\n");

        foreach (var reader in new[] { PdfReader.Open(pdf), PdfReader.Open(new MemoryStream(pdf)) })
        {
            reader.PageCount.Should().Be(2);
            reader.GetPage(1).Width.Should().Be(100);
            reader.GetPage(2).Width.Should().Be(300);
        }
    }

    [Fact]
    public void Open_UnseekableStream_ThrowsArgumentException()
    {
        var unseekable = new System.IO.Compression.DeflateStream(new MemoryStream(), System.IO.Compression.CompressionMode.Decompress);

        Action act = () => PdfReader.Open(unseekable);

        act.Should().Throw<ArgumentException>();
    }
}
