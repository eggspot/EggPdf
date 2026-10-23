using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace EggPdf.Pdf;

internal sealed class PdfObjectStore
{
    private const int MaxObjectNumber = 8_388_607;
    private const int MaxCrossReferenceSections = 1024;
    private const int MaxObjectsPerObjectStream = 1_000_000;
    private const int MaxReferenceChain = 32;
    private const int MaxNestedReads = 16;
    private const int MaxNestedLengthReads = 4;
    private const long MaxDecodedBytesPerDocument = 256L * 1024 * 1024;
    private const int RepairDictionaryWindow = 4096;

    private enum EntryKind { Free, InFile, InObjectStream }

    private readonly struct Entry
    {
        public Entry(EntryKind kind, long offset, int objectStream)
        {
            Kind = kind;
            Offset = offset;
            ObjectStream = objectStream;
        }

        public EntryKind Kind { get; }
        public long Offset { get; }
        public int ObjectStream { get; }
    }

    private sealed class ObjectStreamLayout
    {
        public ObjectStreamLayout(byte[] data, int first, Dictionary<int, int> offsets)
        {
            Data = data;
            First = first;
            Offsets = offsets;
        }

        public byte[] Data { get; }
        public int First { get; }
        public Dictionary<int, int> Offsets { get; }
    }

    private readonly byte[] _data;
    private readonly Dictionary<int, Entry> _entries = new();
    private readonly Dictionary<int, PdfObject> _cache = new();
    private readonly Dictionary<int, ObjectStreamLayout> _objectStreams = new();
    private readonly HashSet<int> _resolving = new();
    private Dictionary<int, long>? _scannedOffsets;
    private int[]? _endstreamOffsets;
    private long _decodedBytes;

    private PdfObjectStore(byte[] data) => _data = data;

    public PdfDictionary Trailer { get; } = new();

    public bool IsEncrypted => Trailer.Get("Encrypt") is { } encrypt && encrypt is not PdfNull;

    public static PdfObjectStore Load(byte[] data)
    {
        var store = new PdfObjectStore(data);
        if (!store.TryLoadCrossReferences() || !store.HasReadableCatalog())
        {
            store = new PdfObjectStore(data);
            store.RebuildFromObjectScan();
        }
        if (store.FindCatalog() == null) throw new PdfFormatException("No document catalog found: this isn't a readable PDF.");
        return store;
    }

    public PdfDictionary? FindCatalog() => Resolve(Trailer.Get("Root")) as PdfDictionary;

    public PdfObject? Resolve(PdfObject? value)
    {
        for (int hops = 0; value is PdfReference reference; hops++)
        {
            if (hops >= MaxReferenceChain) throw new PdfFormatException("A chain of object references is too long.");
            value = Get(reference.ObjectNumber);
        }
        return value;
    }

    public PdfObject Get(int number)
    {
        if (_cache.TryGetValue(number, out var cached)) return cached;
        if (!_entries.TryGetValue(number, out var entry) || entry.Kind == EntryKind.Free) return PdfNull.Instance;
        if (_resolving.Count >= MaxNestedReads) throw new PdfFormatException("Objects depend on each other too deeply.");
        RuntimeHelpers.EnsureSufficientExecutionStack();
        if (!_resolving.Add(number)) throw new PdfFormatException("An object refers to itself while being read.");
        try
        {
            var value = entry.Kind == EntryKind.InFile
                ? ReadObjectAt(entry.Offset, number)
                : ReadFromObjectStream(entry.ObjectStream, number);
            _cache[number] = value;
            return value;
        }
        finally
        {
            _resolving.Remove(number);
        }
    }

    private bool HasReadableCatalog()
    {
        try
        {
            return FindCatalog() != null;
        }
        catch (PdfFormatException)
        {
            return false;
        }
    }

    private bool TryLoadCrossReferences()
    {
        try
        {
            int startxref = PdfSyntaxParser.LastIndexOf(_data, "startxref", _data.Length);
            if (startxref < 0) return false;
            long offset = new PdfSyntaxParser(_data, startxref + "startxref".Length, _data.Length).ReadInteger();

            var visited = new HashSet<long>();
            while (offset >= 0 && visited.Add(offset))
            {
                if (visited.Count > MaxCrossReferenceSections) throw new PdfFormatException("Too many cross-reference sections.");
                if (offset >= _data.Length) return false;
                var freedInRevision = new List<int>();
                var trailer = ReadSection(offset, freedInRevision);
                MergeTrailer(trailer);
                if (trailer.Get("XRefStm") is PdfNumber hybrid && hybrid.IsInteger && visited.Add((long)hybrid.Value))
                    ReadCrossReferenceStream((long)hybrid.Value, freedInRevision);
                foreach (var number in freedInRevision)
                    if (!_entries.ContainsKey(number)) _entries[number] = default;
                offset = trailer.Get("Prev") is PdfNumber previous && previous.IsInteger ? (long)previous.Value : -1;
            }
            return Trailer.Get("Root") != null;
        }
        catch (PdfFormatException)
        {
            return false;
        }
    }

    private PdfDictionary ReadSection(long offset, List<int> freedInRevision)
    {
        var parser = NewParser((int)offset);
        return parser.TryReadKeyword("xref") ? ReadClassicTable(parser, freedInRevision) : ReadCrossReferenceStream(offset, freedInRevision);
    }

    private PdfDictionary ReadClassicTable(PdfSyntaxParser parser, List<int> freedInRevision)
    {
        while (!parser.TryReadKeyword("trailer"))
        {
            long first = parser.ReadInteger();
            long count = parser.ReadInteger();
            if (first < 0 || count < 0 || first + count > MaxObjectNumber + 1)
                throw new PdfFormatException("Cross-reference subsection out of range.");
            for (long i = 0; i < count; i++)
            {
                long offset = parser.ReadInteger();
                parser.ReadInteger();
                bool inUse = parser.TryReadKeyword("n");
                if (!inUse && !parser.TryReadKeyword("f")) throw new PdfFormatException("Malformed cross-reference entry.");
                int number = (int)(first + i);
                if (inUse) AddEntry(number, new Entry(EntryKind.InFile, offset, 0));
                else freedInRevision.Add(number);
            }
        }
        return parser.ReadObject() as PdfDictionary ?? throw new PdfFormatException("The trailer isn't a dictionary.");
    }

    private PdfDictionary ReadCrossReferenceStream(long offset, List<int> freedInRevision)
    {
        if (offset < 0 || offset >= _data.Length) throw new PdfFormatException("Cross-reference stream offset out of range.");
        var parser = NewParser((int)offset);
        if (!parser.TryReadIndirectHeader(out _, out _) || parser.ReadIndirectObjectBody() is not PdfStream stream)
            throw new PdfFormatException("No cross-reference stream at the given offset.");

        var dictionary = stream.Dictionary;
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
        int position = 0;
        foreach (var (first, count) in subsections)
        {
            if (first < 0 || count < 0 || first + count > MaxObjectNumber + 1)
                throw new PdfFormatException("Cross-reference stream subsection out of range.");
            for (long i = 0; i < count && position + rowLength <= rows.Length; i++)
            {
                long type = widths[0] == 0 ? 1 : ReadField(rows, ref position, widths[0]);
                long field2 = ReadField(rows, ref position, widths[1]);
                ReadField(rows, ref position, widths[2]);
                int number = (int)(first + i);
                if (type == 1) AddEntry(number, new Entry(EntryKind.InFile, field2, 0));
                else if (type == 2 && field2 > 0 && field2 <= MaxObjectNumber) AddEntry(number, new Entry(EntryKind.InObjectStream, 0, (int)field2));
                else freedInRevision.Add(number);
            }
        }
        return dictionary;
    }

    private static long ReadField(byte[] rows, ref int position, int width)
    {
        long value = 0;
        for (int i = 0; i < width; i++) value = (value << 8) | rows[position++];
        return value;
    }

    private void AddEntry(int number, Entry entry)
    {
        if (number <= 0 || number > MaxObjectNumber || _entries.ContainsKey(number)) return;
        _entries[number] = entry;
    }

    private void MergeTrailer(PdfDictionary trailer)
    {
        foreach (var pair in trailer.Entries)
            if (Trailer.Get(pair.Key) == null) Trailer.Set(pair.Key, pair.Value);
    }

    private void RebuildFromObjectScan()
    {
        var scanned = ScanObjectOffsets();
        foreach (var pair in scanned)
            _entries[pair.Key] = new Entry(EntryKind.InFile, pair.Value, 0);

        for (int at = PdfSyntaxParser.LastIndexOf(_data, "trailer", _data.Length); at >= 0;
             at = PdfSyntaxParser.LastIndexOf(_data, "trailer", at))
        {
            try
            {
                if (NewParser(at + "trailer".Length).ReadObject() is PdfDictionary trailer) MergeTrailer(trailer);
            }
            catch (PdfFormatException)
            {
            }
        }

        var byOffset = new List<KeyValuePair<int, long>>(scanned);
        byOffset.Sort((a, b) => a.Value.CompareTo(b.Value));
        for (int i = 0; i < byOffset.Count; i++)
        {
            int number = byOffset[i].Key;
            int start = (int)byOffset[i].Value;
            int end = i + 1 < byOffset.Count ? (int)byOffset[i + 1].Value : _data.Length;
            end = Math.Min(end, start + RepairDictionaryWindow);
            bool catalog = PdfSyntaxParser.IndexOf(_data, "/Catalog", start, end) >= 0;
            bool objectStream = PdfSyntaxParser.IndexOf(_data, "/ObjStm", start, end) >= 0;
            bool crossReference = PdfSyntaxParser.IndexOf(_data, "/XRef", start, end) >= 0;
            if (catalog || objectStream || crossReference) InspectForRepair(number);
        }
    }

    private void InspectForRepair(int number)
    {
        PdfObject value;
        try
        {
            value = Get(number);
        }
        catch (PdfFormatException)
        {
            return;
        }
        catch (NotSupportedException)
        {
            return;
        }
        var dictionary = value as PdfDictionary ?? (value as PdfStream)?.Dictionary;
        if (dictionary?.Get("Type") is not PdfName type) return;
        if (type.Value == "Catalog" && Trailer.Get("Root") == null) Trailer.Set("Root", new PdfReference(number, 0));
        if (type.Value == "XRef")
        {
            if (Trailer.Get("Root") == null && dictionary.Get("Root") is { } root) Trailer.Set("Root", root);
            if (Trailer.Get("Encrypt") == null && dictionary.Get("Encrypt") is { } encrypt) Trailer.Set("Encrypt", encrypt);
        }
        if (type.Value == "ObjStm") RegisterObjectStreamContents(number);
    }

    private void RegisterObjectStreamContents(int streamNumber)
    {
        try
        {
            foreach (var number in ObjectStreamOffsets(streamNumber).Offsets.Keys)
                if (!_entries.ContainsKey(number)) _entries[number] = new Entry(EntryKind.InObjectStream, 0, streamNumber);
        }
        catch (PdfFormatException)
        {
        }
    }

    private Dictionary<int, long> ScanObjectOffsets()
    {
        if (_scannedOffsets != null) return _scannedOffsets;
        var offsets = new Dictionary<int, long>();
        for (int i = 0; i < _data.Length; i++)
        {
            if (i > 0 && _data[i - 1] != '\n' && _data[i - 1] != '\r') continue;
            if (_data[i] < '0' || _data[i] > '9') continue;
            var parser = new PdfSyntaxParser(_data, i, _data.Length);
            if (parser.TryReadIndirectHeader(out int number, out _) && number <= MaxObjectNumber)
                offsets[number] = i;
        }
        return _scannedOffsets = offsets;
    }

    private int NextEndstream(int from)
    {
        if (_endstreamOffsets == null)
        {
            var found = new List<int>();
            for (int at = PdfSyntaxParser.IndexOf(_data, "endstream", 0, _data.Length); at >= 0;
                 at = PdfSyntaxParser.IndexOf(_data, "endstream", at + 1, _data.Length))
                found.Add(at);
            _endstreamOffsets = found.ToArray();
        }
        int index = Array.BinarySearch(_endstreamOffsets, from);
        if (index < 0) index = ~index;
        return index < _endstreamOffsets.Length ? _endstreamOffsets[index] : -1;
    }

    private PdfSyntaxParser NewParser(int start) => new(_data, start, _data.Length, ResolveLength, NextEndstream);

    private PdfObject ReadObjectAt(long offset, int expectedNumber)
    {
        if (offset >= 0 && offset < _data.Length && TryParseIndirectObject(offset, expectedNumber, out var value)) return value!;
        if (ScanObjectOffsets().TryGetValue(expectedNumber, out var scanned) && scanned != offset &&
            TryParseIndirectObject(scanned, expectedNumber, out value)) return value!;
        throw new PdfFormatException($"Object {expectedNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)} isn't where the cross-reference says.");
    }

    private bool TryParseIndirectObject(long offset, int expectedNumber, out PdfObject? value)
    {
        value = null;
        var parser = NewParser((int)offset);
        if (!parser.TryReadIndirectHeader(out int number, out _) || number != expectedNumber) return false;
        value = parser.ReadIndirectObjectBody();
        return true;
    }

    private PdfObject? ResolveLength(PdfReference reference) =>
        _resolving.Count >= MaxNestedLengthReads ? null : Get(reference.ObjectNumber);

    private byte[] Decode(PdfStream stream)
    {
        var decoded = PdfStreamDecoder.Decode(stream);
        _decodedBytes += decoded.Length;
        if (_decodedBytes > MaxDecodedBytesPerDocument)
            throw new PdfFormatException("The document's compressed structure expands beyond the 256 MB limit.");
        return decoded;
    }

    private PdfObject ReadFromObjectStream(int streamNumber, int number)
    {
        if (IsEncrypted) throw new NotSupportedException("Objects inside an encrypted PDF's object streams can't be read.");
        var layout = ObjectStreamOffsets(streamNumber);
        if (!layout.Offsets.TryGetValue(number, out int offset)) return PdfNull.Instance;
        return new PdfSyntaxParser(layout.Data, layout.First + offset, layout.Data.Length).ReadObject();
    }

    private ObjectStreamLayout ObjectStreamOffsets(int streamNumber)
    {
        if (_objectStreams.TryGetValue(streamNumber, out var layout)) return layout;
        if (_entries.TryGetValue(streamNumber, out var entry) && entry.Kind == EntryKind.InObjectStream)
            throw new PdfFormatException("An object stream is stored inside another object stream.");
        if (Get(streamNumber) is not PdfStream stream || stream.Dictionary.Get("Type") is not PdfName { Value: "ObjStm" })
            throw new PdfFormatException("A compressed object points at something that isn't an object stream.");

        int count = PdfStreamDecoder.IntegerOr(stream.Dictionary.Get("N"), -1);
        int first = PdfStreamDecoder.IntegerOr(stream.Dictionary.Get("First"), -1);
        var data = Decode(stream);
        if (count < 0 || count > MaxObjectsPerObjectStream || first < 0 || first > data.Length)
            throw new PdfFormatException("An object stream has an invalid /N or /First.");

        var offsets = new Dictionary<int, int>();
        var header = new PdfSyntaxParser(data, 0, first);
        long previousOffset = -1;
        for (int i = 0; i < count; i++)
        {
            long number = header.ReadInteger();
            long offset = header.ReadInteger();
            if (number <= 0 || number > MaxObjectNumber || offset <= previousOffset || first + offset >= data.Length) continue;
            previousOffset = offset;
            if (!offsets.ContainsKey((int)number)) offsets[(int)number] = (int)offset;
        }
        layout = new ObjectStreamLayout(data, first, offsets);
        _objectStreams[streamNumber] = layout;
        return layout;
    }
}
