using System;
using System.Collections.Generic;

namespace EggPdf.Pdf;

internal sealed partial class PdfObjectStore
{
    private const int MaxCrossReferenceSections = 1024;
    private const int MinCrossReferenceEntryBudget = 100_000;
    private const int MaxRowLineBreak = 3;

    private bool TryLoadCrossReferences()
    {
        try
        {
            int startxref = PdfSyntaxParser.LastIndexOf(_data, "startxref", _data.Length);
            if (startxref < 0) return false;
            long offset = new PdfSyntaxParser(_data, startxref + "startxref".Length, _data.Length).ReadInteger();

            _crossReference = new PdfCrossReference();
            var visited = new HashSet<long>();
            while (offset >= 0 && visited.Add(offset))
            {
                if (visited.Count > MaxCrossReferenceSections) throw new PdfFormatException("Too many cross-reference sections.");
                if (offset >= _data.Length) return false;
                var parser = NewParser((int)offset);
                PdfDictionary trailer;
                if (parser.TryReadKeyword("xref"))
                {
                    var table = ReadClassicTable(parser, out trailer);
                    PdfCrossReferenceSection? hybrid = null;
                    if (trailer.Get("XRefStm") is PdfNumber stream && stream.IsInteger && visited.Add((long)stream.Value))
                        hybrid = ReadCrossReferenceStream((long)stream.Value, out _);
                    _crossReference.AddRevision(table, hybrid);
                }
                else
                {
                    _crossReference.AddRevision(null, ReadCrossReferenceStream(offset, out trailer));
                }
                if (visited.Count == 1)
                {
                    LastCrossReferenceOffset = offset;
                    LastCrossReferenceIsStream = trailer.Get("Type") is PdfName { Value: "XRef" };
                }
                MergeTrailer(trailer);
                offset = trailer.Get("Prev") is PdfNumber previous && previous.IsInteger ? (long)previous.Value : -1;
            }
            _crossReference.Seal();
            return Trailer.Get("Root") != null;
        }
        catch (PdfFormatException)
        {
            return false;
        }
    }

    private PdfCrossReferenceSection ReadClassicTable(PdfSyntaxParser parser, out PdfDictionary trailer)
    {
        var section = new PdfCrossReferenceSection();
        while (!parser.TryReadKeyword("trailer"))
        {
            long first = parser.ReadInteger();
            long count = parser.ReadInteger();
            if (first < 0 || count < 0 || first + count > PdfCrossReference.MaxObjectNumber + 1)
                throw new PdfFormatException("Cross-reference subsection out of range.");
            parser.SkipWhitespaceAndComments();
            int rowsStart = parser.Position;
            if (count > 0 && TryMeasureRowStride(rowsStart, count, out int stride))
            {
                section.Add(new PdfClassicRows(_data, (int)first, (int)count, rowsStart, stride));
                parser.Position = (int)(rowsStart + (long)stride * (count - 1) + PdfClassicRows.RowTextLength);
            }
            else
            {
                section.Add(new PdfParsedRows((int)first, ParseRows(parser, count)));
            }
        }
        trailer = parser.ReadObject() as PdfDictionary ?? throw new PdfFormatException("The trailer isn't a dictionary.");
        return section;
    }

    private bool TryMeasureRowStride(int rowsStart, long count, out int stride)
    {
        stride = 0;
        if (!PdfClassicRows.TryReadRow(_data, rowsStart, out _, out _)) return false;
        int lineBreak = 0;
        int after = rowsStart + PdfClassicRows.RowTextLength;
        while (lineBreak < MaxRowLineBreak && after + lineBreak < _data.Length && PdfSyntaxParser.IsWhitespace(_data[after + lineBreak]))
            lineBreak++;
        if (lineBreak == 0) return false;
        stride = PdfClassicRows.RowTextLength + lineBreak;
        if (count == 1) return true;
        long last = rowsStart + (long)stride * (count - 1);
        return PdfClassicRows.TryReadRow(_data, rowsStart + stride, out _, out _) && PdfClassicRows.TryReadRow(_data, last, out _, out _);
    }

    private PdfCrossReferenceEntry[] ParseRows(PdfSyntaxParser parser, long count)
    {
        var entries = new List<PdfCrossReferenceEntry>();
        for (long i = 0; i < count; i++)
        {
            SpendEntryBudget(1);
            long offset = parser.ReadInteger();
            parser.ReadInteger();
            bool inUse = parser.TryReadKeyword("n");
            if (!inUse && !parser.TryReadKeyword("f")) throw new PdfFormatException("Malformed cross-reference entry.");
            entries.Add(inUse ? new PdfCrossReferenceEntry(PdfCrossReferenceEntryKind.InFile, offset, 0) : PdfCrossReferenceEntry.Free);
        }
        return entries.ToArray();
    }

    private PdfCrossReferenceSection ReadCrossReferenceStream(long offset, out PdfDictionary dictionary)
    {
        if (offset < 0 || offset >= _data.Length) throw new PdfFormatException("Cross-reference stream offset out of range.");
        var parser = NewParser((int)offset);
        if (!parser.TryReadIndirectHeader(out _, out _) || parser.ReadIndirectObjectBody() is not PdfStream stream)
            throw new PdfFormatException("No cross-reference stream at the given offset.");

        dictionary = stream.Dictionary;
        if (dictionary.Get("W") is not PdfArray widthArray || widthArray.Items.Count != 3)
            throw new PdfFormatException("Cross-reference stream has no valid /W.");
        var widths = new int[3];
        for (int i = 0; i < 3; i++)
        {
            widths[i] = PdfStreamDecoder.IntegerOr(widthArray.Items[i], -1);
            if (widths[i] < 0 || widths[i] > 8) throw new PdfFormatException("Cross-reference stream has an invalid /W.");
        }
        int rowLength = widths[0] + widths[1] + widths[2];
        if (rowLength == 0) throw new PdfFormatException("Cross-reference stream rows are empty.");

        var subsections = new List<(long First, long Count)>();
        if (dictionary.Get("Index") is PdfArray index)
        {
            for (int i = 0; i + 1 < index.Items.Count; i += 2)
                subsections.Add((PdfStreamDecoder.IntegerOr(index.Items[i], -1), PdfStreamDecoder.IntegerOr(index.Items[i + 1], -1)));
        }
        else
        {
            subsections.Add((0, PdfStreamDecoder.IntegerOr(dictionary.Get("Size"), -1)));
        }

        var rows = Decode(stream);
        int rowCount = rows.Length / rowLength;
        int nextRow = 0;
        var section = new PdfCrossReferenceSection();
        foreach (var (first, count) in subsections)
        {
            if (first < 0 || count < 0 || first + count > PdfCrossReference.MaxObjectNumber + 1)
                throw new PdfFormatException("Cross-reference stream subsection out of range.");
            int usable = (int)Math.Min(count, rowCount - nextRow);
            if (usable <= 0) continue;
            SpendEntryBudget(usable);
            section.Add(new PdfStreamRows(rows, (int)first, usable, nextRow, widths));
            nextRow += usable;
        }
        return section;
    }

    private void SpendEntryBudget(long entries)
    {
        _entryBudget -= entries;
        if (_entryBudget < 0) throw new PdfFormatException("The cross-reference declares more objects than the file can hold.");
    }

    private void MergeTrailer(PdfDictionary trailer)
    {
        foreach (var pair in trailer.Entries)
            if (Trailer.Get(pair.Key) == null) Trailer.Set(pair.Key, pair.Value);
    }
}
