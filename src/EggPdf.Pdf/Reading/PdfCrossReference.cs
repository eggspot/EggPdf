using System;
using System.Collections.Generic;

namespace EggPdf.Pdf;

internal enum PdfCrossReferenceEntryKind { Free, InFile, InObjectStream }

internal readonly struct PdfCrossReferenceEntry
{
    public PdfCrossReferenceEntry(PdfCrossReferenceEntryKind kind, long offset, int objectStream)
    {
        Kind = kind;
        Offset = offset;
        ObjectStream = objectStream;
    }

    public PdfCrossReferenceEntryKind Kind { get; }
    public long Offset { get; }
    public int ObjectStream { get; }

    public static PdfCrossReferenceEntry Free => default;
}

internal abstract class PdfCrossReferenceSubsection
{
    protected PdfCrossReferenceSubsection(int first, int count)
    {
        First = first;
        Count = count;
    }

    public int First { get; }
    public int Count { get; }

    public abstract PdfCrossReferenceEntry Read(int index);
}

internal sealed class PdfClassicRows : PdfCrossReferenceSubsection
{
    public const int RowTextLength = 18;

    private readonly byte[] _data;
    private readonly long _rowsStart;
    private readonly int _stride;

    public PdfClassicRows(byte[] data, int first, int count, long rowsStart, int stride) : base(first, count)
    {
        _data = data;
        _rowsStart = rowsStart;
        _stride = stride;
    }

    public override PdfCrossReferenceEntry Read(int index)
    {
        long at = _rowsStart + (long)_stride * index;
        if (!TryReadRow(_data, at, out long offset, out bool inUse))
            return new PdfCrossReferenceEntry(PdfCrossReferenceEntryKind.InFile, -1, 0);
        return inUse ? new PdfCrossReferenceEntry(PdfCrossReferenceEntryKind.InFile, offset, 0) : PdfCrossReferenceEntry.Free;
    }

    public static bool TryReadRow(byte[] data, long at, out long offset, out bool inUse)
    {
        offset = 0;
        inUse = false;
        if (at < 0 || at + RowTextLength > data.Length) return false;
        int start = (int)at;
        for (int i = 0; i < 10; i++)
        {
            byte b = data[start + i];
            if (b < '0' || b > '9') return false;
            offset = offset * 10 + (b - '0');
        }
        if (data[start + 10] != ' ' || data[start + 16] != ' ') return false;
        for (int i = 11; i < 16; i++)
            if (data[start + i] < '0' || data[start + i] > '9') return false;
        byte type = data[start + 17];
        if (type != 'n' && type != 'f') return false;
        inUse = type == 'n';
        return true;
    }
}

internal sealed class PdfParsedRows : PdfCrossReferenceSubsection
{
    private readonly PdfCrossReferenceEntry[] _entries;

    public PdfParsedRows(int first, PdfCrossReferenceEntry[] entries) : base(first, entries.Length) => _entries = entries;

    public override PdfCrossReferenceEntry Read(int index) => _entries[index];
}

internal sealed class PdfStreamRows : PdfCrossReferenceSubsection
{
    private readonly byte[] _rows;
    private readonly int _firstRow;
    private readonly int[] _widths;
    private readonly int _rowLength;

    public PdfStreamRows(byte[] rows, int first, int count, int firstRow, int[] widths) : base(first, count)
    {
        _rows = rows;
        _firstRow = firstRow;
        _widths = widths;
        _rowLength = widths[0] + widths[1] + widths[2];
    }

    public override PdfCrossReferenceEntry Read(int index)
    {
        int position = (_firstRow + index) * _rowLength;
        long type = _widths[0] == 0 ? 1 : Field(ref position, _widths[0]);
        long field2 = Field(ref position, _widths[1]);
        if (type == 1) return new PdfCrossReferenceEntry(PdfCrossReferenceEntryKind.InFile, field2, 0);
        if (type == 2 && field2 > 0 && field2 <= PdfCrossReference.MaxObjectNumber)
            return new PdfCrossReferenceEntry(PdfCrossReferenceEntryKind.InObjectStream, 0, (int)field2);
        return PdfCrossReferenceEntry.Free;
    }

    private long Field(ref int position, int width)
    {
        long value = 0;
        for (int i = 0; i < width; i++) value = (value << 8) | _rows[position++];
        return value;
    }
}

internal sealed class PdfCrossReferenceSection
{
    private readonly List<PdfCrossReferenceSubsection> _inFileOrder = new();
    private PdfCrossReferenceSubsection[]? _byFirst;
    private bool _overlapping;

    public void Add(PdfCrossReferenceSubsection subsection)
    {
        if (subsection.Count == 0) return;
        _inFileOrder.Add(subsection);
        _byFirst = null;
    }

    public int HighestObjectNumber
    {
        get
        {
            int highest = 0;
            foreach (var subsection in _inFileOrder) highest = Math.Max(highest, subsection.First + subsection.Count - 1);
            return highest;
        }
    }

    public bool TryFind(int number, out PdfCrossReferenceEntry entry)
    {
        if (_byFirst == null) Sort();
        if (_overlapping)
        {
            foreach (var subsection in _inFileOrder)
                if (TryRead(subsection, number, out entry)) return true;
            entry = default;
            return false;
        }

        int low = 0, high = _byFirst!.Length - 1;
        while (low <= high)
        {
            int middle = (low + high) >> 1;
            if (_byFirst[middle].First <= number) low = middle + 1;
            else high = middle - 1;
        }
        if (high >= 0 && TryRead(_byFirst[high], number, out entry)) return true;
        entry = default;
        return false;
    }

    private static bool TryRead(PdfCrossReferenceSubsection subsection, int number, out PdfCrossReferenceEntry entry)
    {
        long index = (long)number - subsection.First;
        if (index < 0 || index >= subsection.Count)
        {
            entry = default;
            return false;
        }
        entry = subsection.Read((int)index);
        return true;
    }

    private void Sort()
    {
        var sorted = _inFileOrder.ToArray();
        Array.Sort(sorted, (a, b) => a.First.CompareTo(b.First));
        _overlapping = false;
        for (int i = 0; i + 1 < sorted.Length; i++)
            if ((long)sorted[i].First + sorted[i].Count > sorted[i + 1].First) _overlapping = true;
        _byFirst = sorted;
    }
}

internal sealed class PdfCrossReference
{
    public const int MaxObjectNumber = 8_388_607;

    private sealed class Revision
    {
        public PdfCrossReferenceSection? Table { get; set; }
        public PdfCrossReferenceSection? Stream { get; set; }
    }

    private readonly List<Revision> _newestFirst = new();

    public void AddRevision(PdfCrossReferenceSection? table, PdfCrossReferenceSection? stream) =>
        _newestFirst.Add(new Revision { Table = table, Stream = stream });

    public int HighestObjectNumber
    {
        get
        {
            int highest = 0;
            foreach (var revision in _newestFirst)
            {
                if (revision.Table != null) highest = Math.Max(highest, revision.Table.HighestObjectNumber);
                if (revision.Stream != null) highest = Math.Max(highest, revision.Stream.HighestObjectNumber);
            }
            return highest;
        }
    }

    public bool TryFind(int number, out PdfCrossReferenceEntry entry)
    {
        foreach (var revision in _newestFirst)
        {
            bool freed = false;
            if (revision.Table != null && revision.Table.TryFind(number, out entry))
            {
                if (entry.Kind != PdfCrossReferenceEntryKind.Free) return true;
                freed = true;
            }
            if (revision.Stream != null && revision.Stream.TryFind(number, out entry))
            {
                if (entry.Kind != PdfCrossReferenceEntryKind.Free) return true;
                freed = true;
            }
            if (freed)
            {
                entry = PdfCrossReferenceEntry.Free;
                return true;
            }
        }
        entry = default;
        return false;
    }
}
