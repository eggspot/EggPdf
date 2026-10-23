using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EggPdf.Pdf;

internal sealed class PdfIncrementalUpdate
{
    private const int MaxObjectNumber = 8_388_607;
    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);

    private readonly MemoryStream _output = new();
    private readonly PdfObjectStore _store;
    private readonly List<(int Number, int Generation, long Offset)> _written = new();
    private int _nextNumber;

    public PdfIncrementalUpdate(byte[] original, PdfObjectStore store)
    {
        _store = store;
        _output.Write(original, 0, original.Length);
        if (original.Length > 0 && original[original.Length - 1] != '\n' && original[original.Length - 1] != '\r')
            _output.WriteByte((byte)'\n');
        int declaredSize = PdfStreamDecoder.IntegerOr(store.Trailer.Get("Size"), 0);
        _nextNumber = Math.Max(store.HighestObjectNumber + 1, declaredSize);
        if (_nextNumber > MaxObjectNumber + 1)
            throw new PdfFormatException("The PDF's trailer /Size is beyond the 8,388,607 objects a PDF can hold.");
    }

    public int Allocate()
    {
        if (_nextNumber > MaxObjectNumber)
            throw new PdfFormatException("The update would need more than the 8,388,607 objects a PDF can hold.");
        return _nextNumber++;
    }

    public void WriteObject(int number, int generation, PdfObject value) =>
        Write(number, generation, PdfObjectSerializer.Serialize(value), null);

    public void WriteStream(int number, string dictionaryEntries, byte[] data) =>
        Write(number, 0, "<< " + dictionaryEntries + " /Length " + data.Length.ToString(CultureInfo.InvariantCulture) + " >>", data);

    public byte[] Finish()
    {
        if (_store.LastCrossReferenceIsStream) WriteCrossReferenceStream();
        else WriteCrossReferenceTable();
        return _output.ToArray();
    }

    private void Write(int number, int generation, string body, byte[]? streamData)
    {
        _written.Add((number, generation, _output.Position));
        WriteText(number.ToString(CultureInfo.InvariantCulture) + " " + generation.ToString(CultureInfo.InvariantCulture) + " obj\n" + body + "\n");
        if (streamData != null)
        {
            WriteText("stream\n");
            _output.Write(streamData, 0, streamData.Length);
            WriteText("\nendstream\n");
        }
        WriteText("endobj\n");
    }

    private void WriteCrossReferenceTable()
    {
        long offset = _output.Position;
        var table = new StringBuilder("xref\n");
        foreach (var (first, entries) in Subsections())
        {
            table.Append(first.ToString(CultureInfo.InvariantCulture)).Append(' ')
                 .Append(entries.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (var (_, generation, objectOffset) in entries)
                table.Append(objectOffset.ToString("D10", CultureInfo.InvariantCulture)).Append(' ')
                     .Append(generation.ToString("D5", CultureInfo.InvariantCulture)).Append(" n \n");
        }
        table.Append("trailer\n").Append(PdfObjectSerializer.Serialize(Trailer(null))).Append('\n');
        WriteText(table.ToString());
        WriteText("startxref\n" + offset.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
    }

    private void WriteCrossReferenceStream()
    {
        int number = Allocate();
        long offset = _output.Position;
        _written.Add((number, 0, offset));
        var rows = new MemoryStream();
        var index = new PdfArray();
        foreach (var (first, entries) in Subsections())
        {
            index.Items.Add(new PdfNumber(first));
            index.Items.Add(new PdfNumber(entries.Count));
            foreach (var (_, generation, objectOffset) in entries)
            {
                rows.WriteByte(1);
                for (int shift = 24; shift >= 0; shift -= 8) rows.WriteByte((byte)(objectOffset >> shift));
                rows.WriteByte((byte)(generation >> 8));
                rows.WriteByte((byte)generation);
            }
        }
        var dictionary = Trailer(index);
        dictionary.Set("Type", new PdfName("XRef"));
        dictionary.Set("W", new PdfArray { Items = { new PdfNumber(1), new PdfNumber(4), new PdfNumber(2) } });
        var data = rows.ToArray();
        dictionary.Set("Length", new PdfNumber(data.Length));
        WriteText(number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n" + PdfObjectSerializer.Serialize(dictionary) + "\nstream\n");
        _output.Write(data, 0, data.Length);
        WriteText("\nendstream\nendobj\nstartxref\n" + offset.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
    }

    private PdfDictionary Trailer(PdfArray? index)
    {
        var trailer = new PdfDictionary();
        trailer.Set("Size", new PdfNumber(_nextNumber));
        if (index != null) trailer.Set("Index", index);
        foreach (var key in new[] { "Root", "Info", "ID" })
            if (_store.Trailer.Get(key) is { } value) trailer.Set(key, value);
        trailer.Set("Prev", new PdfNumber(_store.LastCrossReferenceOffset));
        return trailer;
    }

    private List<(int First, List<(int Number, int Generation, long Offset)> Entries)> Subsections()
    {
        var sorted = new List<(int Number, int Generation, long Offset)>(_written);
        sorted.Sort((a, b) => a.Number.CompareTo(b.Number));
        var subsections = new List<(int, List<(int, int, long)>)>();
        foreach (var entry in sorted)
        {
            if (subsections.Count > 0)
            {
                var (first, entries) = subsections[subsections.Count - 1];
                if (first + entries.Count == entry.Number)
                {
                    entries.Add(entry);
                    continue;
                }
            }
            subsections.Add((entry.Number, new List<(int, int, long)> { entry }));
        }
        return subsections;
    }

    private void WriteText(string text)
    {
        var bytes = Latin1.GetBytes(text);
        _output.Write(bytes, 0, bytes.Length);
    }
}
