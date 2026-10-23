using System.Globalization;
using System.IO;
using System.Text;
using BenchmarkDotNet.Attributes;
using EggPdf.Pdf;

namespace EggPdf.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ReaderBenchmarks
{
    private const int Pages = 150_000;
    private const int PagesPerNode = 100;

    private byte[] _balanced = null!;
    private byte[] _flat = null!;

    [GlobalSetup]
    public void Setup()
    {
        _balanced = Build(balanced: true);
        _flat = Build(balanced: false);
    }

    [Benchmark(Description = "Page count, 150k pages, balanced tree")]
    public int CountBalanced() => PdfReader.GetPageCount(_balanced);

    [Benchmark(Description = "Page count, 150k pages, flat tree")]
    public int CountFlat() => PdfReader.GetPageCount(_flat);

    [Benchmark(Description = "Open + last page, 150k pages, balanced tree")]
    public double LastPageBalanced() => PdfReader.Open(_balanced).GetPage(Pages).Width;

    [Benchmark(Description = "Open + last page from a Stream, 150k pages, balanced tree")]
    public double LastPageBalancedFromStream()
    {
        using var reader = PdfReader.Open(new MemoryStream(_balanced, writable: false));
        return reader.GetPage(Pages).Width;
    }

    private static byte[] Build(bool balanced)
    {
        var bodies = new string[Pages + 3 + (balanced ? Pages / PagesPerNode : 0)];
        bodies[1] = "<< /Type /Catalog /Pages 2 0 R >>";
        int firstPage = 3;
        var rootKids = new StringBuilder();
        if (balanced)
        {
            int nodes = Pages / PagesPerNode;
            firstPage = 3 + nodes;
            for (int n = 0; n < nodes; n++)
            {
                var kids = new StringBuilder();
                for (int p = 0; p < PagesPerNode; p++)
                {
                    int page = firstPage + n * PagesPerNode + p;
                    kids.Append(N(page)).Append(" 0 R ");
                    bodies[page] = Page(3 + n);
                }
                bodies[3 + n] = $"<< /Type /Pages /Parent 2 0 R /Kids [{kids}] /Count {N(PagesPerNode)} >>";
                rootKids.Append(N(3 + n)).Append(" 0 R ");
            }
        }
        else
        {
            for (int p = 0; p < Pages; p++)
            {
                bodies[firstPage + p] = Page(2);
                rootKids.Append(N(firstPage + p)).Append(" 0 R ");
            }
        }
        bodies[2] = $"<< /Type /Pages /Kids [{rootKids}] /Count {N(Pages)} >>";

        var output = new MemoryStream();
        var offsets = new long[bodies.Length];
        Write(output, "%PDF-1.7\n");
        for (int i = 1; i < bodies.Length; i++)
        {
            offsets[i] = output.Position;
            Write(output, $"{N(i)} 0 obj\n{bodies[i]}\nendobj\n");
        }
        long xref = output.Position;
        var table = new StringBuilder($"xref\n0 {N(bodies.Length)}\n0000000000 65535 f \n");
        for (int i = 1; i < bodies.Length; i++) table.Append(offsets[i].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        Write(output, table.ToString());
        Write(output, $"trailer\n<< /Size {N(bodies.Length)} /Root 1 0 R >>\nstartxref\n{N(xref)}\n%%EOF\n");
        return output.ToArray();
    }

    private static string Page(int parent) => $"<< /Type /Page /Parent {N(parent)} 0 R /MediaBox [0 0 612 792] >>";

    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Write(Stream output, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        output.Write(bytes, 0, bytes.Length);
    }
}
