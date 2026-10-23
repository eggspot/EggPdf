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
            long startxref = _source.LastIndexOf("startxref");
            if (startxref < 0) return false;
            long offset = ParseAt(startxref + "startxref".Length, parser => parser.ReadInteger());

            _crossReference = new PdfCrossReference();
            var visited = new HashSet<long>();
            while (offset >= 0 && visited.Add(offset))
            {
                if (visited.Count > MaxCrossReferenceSections) throw new PdfFormatException("Too many cross-reference sections.");
                if (offset >= _length) return false;
                long rowsHeader = ParseAt(offset, parser => parser.TryReadKeyword("xref") ? parser.FilePosition : -1);
                PdfDictionary trailer;
                if (rowsHeader >= 0)
                {
                    var table = ReadClassicTable(rowsHeader, out trailer);
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

    private PdfCrossReferenceSection ReadClassicTable(long position, out PdfDictionary trailer)
    {
        var section = new PdfCrossReferenceSection();
        while (true)
        {
            var step = ParseAt<(PdfDictionary? Trailer, long First, long Count, long RowsStart)>(position, parser =>
            {
                if (parser.TryReadKeyword("trailer"))
                    return (parser.ReadObject() as PdfDictionary ?? throw new PdfFormatException("The trailer isn't a dictionary."), 0, 0, 0);
                long first = parser.ReadInteger();
                long count = parser.ReadInteger();
                parser.SkipWhitespaceAndComments();
                return (null, first, count, parser.FilePosition);
            });
            if (step.Trailer != null)
            {
                trailer = step.Trailer;
                return section;
            }
            if (step.First < 0 || step.Count < 0 || step.First + step.Count > PdfCrossReference.MaxObjectNumber + 1)
                throw new PdfFormatException("Cross-reference subsection out of range.");
            if (step.Count > 0 && TryMeasureRowStride(step.RowsStart, step.Count, out int stride))
            {
                section.Add(new PdfClassicRows(_source, (int)step.First, (int)step.Count, step.RowsStart, stride));
                position = step.RowsStart + stride * (step.Count - 1) + PdfClassicRows.RowTextLength;
            }
            else
            {
                var (entries, end) = ParseAt(step.RowsStart, parser => (ParseRows(parser, step.Count), parser.FilePosition));
                section.Add(new PdfParsedRows((int)step.First, entries));
                position = end;
            }
        }
    }

    private bool TryMeasureRowStride(long rowsStart, long count, out int stride)
    {
        stride = 0;
        if (!PdfClassicRows.TryReadRow(_source, rowsStart, out _, out _)) return false;
        var lineBreakWindow = _source.Window(rowsStart + PdfClassicRows.RowTextLength, MaxRowLineBreak);
        int lineBreak = 0;
        while (lineBreak < MaxRowLineBreak && lineBreakWindow.Offset + lineBreak < lineBreakWindow.End &&
               PdfSyntaxParser.IsWhitespace(lineBreakWindow.Data[lineBreakWindow.Offset + lineBreak]))
            lineBreak++;
        if (lineBreak == 0) return false;
        stride = PdfClassicRows.RowTextLength + lineBreak;
        if (count == 1) return true;
        long last = rowsStart + (long)stride * (count - 1);
        return PdfClassicRows.TryReadRow(_source, rowsStart + stride, out _, out _) && PdfClassicRows.TryReadRow(_source, last, out _, out _);
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
        if (offset < 0 || offset >= _length) throw new PdfFormatException("Cross-reference stream offset out of range.");
        var stream = ParseAt(offset, parser => parser.TryReadIndirectHeader(out _, out _) ? parser.ReadIndirectObjectBody() as PdfStream : null)
            ?? throw new PdfFormatException("No cross-reference stream at the given offset.");

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
