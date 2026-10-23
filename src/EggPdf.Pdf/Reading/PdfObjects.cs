using System;
using System.Collections.Generic;

namespace EggPdf.Pdf;

internal abstract class PdfObject
{
}

internal sealed class PdfNull : PdfObject
{
    public static readonly PdfNull Instance = new();

    private PdfNull()
    {
    }
}

internal sealed class PdfBoolean : PdfObject
{
    public PdfBoolean(bool value) => Value = value;

    public bool Value { get; }
}

internal sealed class PdfNumber : PdfObject
{
    public PdfNumber(double value) => Value = value;

    public double Value { get; }

    public bool IsInteger => Value == Math.Floor(Value) && Math.Abs(Value) <= long.MaxValue;
}

internal sealed class PdfName : PdfObject
{
    public PdfName(string value) => Value = value;

    public string Value { get; }
}

internal sealed class PdfString : PdfObject
{
    public PdfString(byte[] bytes) => Bytes = bytes;

    public byte[] Bytes { get; }
}

internal sealed class PdfArray : PdfObject
{
    public List<PdfObject> Items { get; } = new();
}

internal sealed class PdfDictionary : PdfObject
{
    private readonly Dictionary<string, PdfObject> _entries = new(StringComparer.Ordinal);

    public PdfObject? Get(string key) => _entries.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, PdfObject value) => _entries[key] = value;

    public IEnumerable<KeyValuePair<string, PdfObject>> Entries => _entries;
}

internal sealed class PdfStream : PdfObject
{
    public PdfStream(PdfDictionary dictionary, byte[] source, int dataStart, int dataLength)
    {
        Dictionary = dictionary;
        Source = source;
        DataStart = dataStart;
        DataLength = dataLength;
    }

    public PdfDictionary Dictionary { get; }

    public byte[] Source { get; }

    public int DataStart { get; }

    public int DataLength { get; }
}

internal sealed class PdfReference : PdfObject
{
    public PdfReference(int objectNumber, int generation)
    {
        ObjectNumber = objectNumber;
        Generation = generation;
    }

    public int ObjectNumber { get; }

    public int Generation { get; }
}
