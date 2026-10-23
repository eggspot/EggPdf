using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace EggPdf.Tests.Unit.Pdf.Reading;

internal sealed class TestPdfBuilder
{
    private static readonly Encoding Latin1 = Encoding.GetEncoding("ISO-8859-1");

    private readonly SortedDictionary<int, (string Body, bool InObjectStream)> _objects = new();

    public TestPdfBuilder Add(int number, string body, bool inObjectStream = false)
    {
        _objects[number] = (body, inObjectStream);
        return this;
    }

    public byte[] BuildClassic(int root, string? extraTrailer = null)
    {
        var output = new MemoryStream();
        Write(output, "%PDF-1.7\n%\xE2\xE3\xCF\xD3\n");
        var offsets = new SortedDictionary<int, long>();
        foreach (var (number, (body, _)) in _objects)
        {
            offsets[number] = output.Position;
            Write(output, $"{N(number)} 0 obj\n{body}\nendobj\n");
        }
        long xref = output.Position;
        int size = MaxNumber() + 1;
        Write(output, $"xref\n0 {N(size)}\n0000000000 65535 f \n");
        for (int i = 1; i < size; i++)
            Write(output, offsets.TryGetValue(i, out var o) ? $"{o:D10} 00000 n \n" : "0000000000 00000 f \n");
        Write(output, $"trailer\n<< /Size {N(size)} /Root {N(root)} 0 R {extraTrailer} >>\nstartxref\n{xref.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
        return output.ToArray();
    }

    public byte[] BuildWithXrefStream(int root, bool pngUpPredictor, bool hybrid = false, byte[]? objectStreamData = null)
    {
        var output = new MemoryStream();
        Write(output, "%PDF-1.7\n%\xE2\xE3\xCF\xD3\n");
        var offsets = new SortedDictionary<int, long>();
        var packed = new List<int>();
        foreach (var (number, (body, inObjectStream)) in _objects)
        {
            if (inObjectStream) { packed.Add(number); continue; }
            offsets[number] = output.Position;
            Write(output, $"{N(number)} 0 obj\n{body}\nendobj\n");
        }

        int objectStreamNumber = MaxNumber() + 1;
        int xrefStreamNumber = objectStreamNumber + 1;
        if (packed.Count > 0)
        {
            var header = new StringBuilder();
            var data = new StringBuilder();
            foreach (var number in packed)
            {
                header.Append(N(number)).Append(' ').Append(N(data.Length)).Append(' ');
                data.Append(_objects[number].Body).Append('\n');
            }
            var first = header.Length;
            var raw = Latin1.GetBytes(header.ToString() + data);
            var compressed = objectStreamData ?? Zlib(raw);
            offsets[objectStreamNumber] = output.Position;
            Write(output, $"{N(objectStreamNumber)} 0 obj\n<< /Type /ObjStm /N {N(packed.Count)} /First {N(first)} /Filter /FlateDecode /Length {N(compressed.Length)} >>\nstream\n");
            output.Write(compressed, 0, compressed.Length);
            Write(output, "\nendstream\nendobj\n");
        }

        int size = xrefStreamNumber + 1;
        long xrefOffset = output.Position;
        var rows = new List<byte[]>();
        for (int i = 0; i < size; i++)
        {
            if (i == xrefStreamNumber) rows.Add(Row(1, xrefOffset, 0));
            else if (offsets.TryGetValue(i, out var o)) rows.Add(Row(1, o, 0));
            else if (packed.Contains(i)) rows.Add(Row(2, objectStreamNumber, packed.IndexOf(i)));
            else rows.Add(Row(0, 0, 0));
        }

        if (hybrid)
            return BuildHybrid(output, offsets, rows, size, root, packed);

        var table = Concat(rows, pngUpPredictor);
        var tableCompressed = Zlib(table);
        var decodeParms = pngUpPredictor ? " /DecodeParms << /Predictor 12 /Columns 6 >>" : "";
        Write(output, $"{N(xrefStreamNumber)} 0 obj\n<< /Type /XRef /Size {N(size)} /W [1 4 1] /Root {N(root)} 0 R /Filter /FlateDecode{decodeParms} /Length {N(tableCompressed.Length)} >>\nstream\n");
        output.Write(tableCompressed, 0, tableCompressed.Length);
        Write(output, $"\nendstream\nendobj\nstartxref\n{xrefOffset.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
        return output.ToArray();
    }

    private static byte[] BuildHybrid(MemoryStream output, SortedDictionary<int, long> offsets, List<byte[]> rows, int size, int root,
        List<int> packed)
    {
        int xrefStreamNumber = size - 1;
        long streamOffset = output.Position;
        rows[xrefStreamNumber] = Row(1, streamOffset, 0);
        var tableCompressed = Zlib(Concat(rows, false));
        Write(output, $"{N(xrefStreamNumber)} 0 obj\n<< /Type /XRef /Size {N(size)} /W [1 4 1] /Filter /FlateDecode /Length {N(tableCompressed.Length)} >>\nstream\n");
        output.Write(tableCompressed, 0, tableCompressed.Length);
        Write(output, "\nendstream\nendobj\n");

        long classic = output.Position;
        Write(output, $"xref\n0 {N(size)}\n0000000000 65535 f \n");
        for (int i = 1; i < size; i++)
        {
            if (i == xrefStreamNumber) Write(output, $"{streamOffset:D10} 00000 n \n");
            else if (offsets.TryGetValue(i, out var o) && !packed.Contains(i)) Write(output, $"{o:D10} 00000 n \n");
            else Write(output, "0000000000 00000 f \n");
        }
        Write(output, $"trailer\n<< /Size {N(size)} /Root {N(root)} 0 R /XRefStm {streamOffset.ToString(CultureInfo.InvariantCulture)} >>\nstartxref\n{classic.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
        return output.ToArray();
    }

    public static byte[] AppendUpdate(byte[] original, int root, int size, params (int Number, string Body)[] objects)
    {
        var text = Latin1.GetString(original);
        int startxref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        var previous = text.Substring(startxref + 9).Trim().Split('\n')[0].Trim();

        var output = new MemoryStream();
        output.Write(original, 0, original.Length);
        var offsets = new List<(int, long)>();
        foreach (var (number, body) in objects)
        {
            offsets.Add((number, output.Position));
            Write(output, $"{N(number)} 0 obj\n{body}\nendobj\n");
        }
        long xref = output.Position;
        Write(output, "xref\n");
        foreach (var (number, offset) in offsets)
            Write(output, $"{N(number)} 1\n{offset:D10} 00000 n \n");
        Write(output, $"trailer\n<< /Size {N(size)} /Root {N(root)} 0 R /Prev {previous} >>\nstartxref\n{xref.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
        return output.ToArray();
    }

    public static byte[] Zlib(byte[] raw)
    {
        var output = new MemoryStream();
        output.WriteByte(0x78);
        output.WriteByte(0x9C);
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            deflate.Write(raw, 0, raw.Length);
        output.Write(new byte[4], 0, 4);
        return output.ToArray();
    }

    private int MaxNumber()
    {
        int max = 0;
        foreach (var number in _objects.Keys) max = Math.Max(max, number);
        return max;
    }

    private static byte[] Row(int type, long field2, int field3) =>
        new[] { (byte)type, (byte)(field2 >> 24), (byte)(field2 >> 16), (byte)(field2 >> 8), (byte)field2, (byte)field3 };

    private static byte[] Concat(List<byte[]> rows, bool pngUpPredictor)
    {
        var output = new MemoryStream();
        var previous = new byte[6];
        foreach (var row in rows)
        {
            if (pngUpPredictor)
            {
                output.WriteByte(2);
                for (int i = 0; i < row.Length; i++) output.WriteByte((byte)(row[i] - previous[i]));
                previous = row;
            }
            else
            {
                output.Write(row, 0, row.Length);
            }
        }
        return output.ToArray();
    }

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static void Write(Stream output, string text)
    {
        var bytes = Latin1.GetBytes(text);
        output.Write(bytes, 0, bytes.Length);
    }
}
