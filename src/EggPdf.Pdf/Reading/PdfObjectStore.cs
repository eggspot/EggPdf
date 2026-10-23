using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace EggPdf.Pdf;

internal sealed partial class PdfObjectStore
{
    private const int MaxObjectsPerObjectStream = 1_000_000;
    private const int MaxReferenceChain = 32;
    private const int MaxNestedReads = 16;
    private const int MaxNestedLengthReads = 4;
    private const long MaxDecodedBytesPerOperation = 256L * 1024 * 1024;
    private const long DecodedBytesAllowance = 64L * 1024 * 1024;
    private const int DecodedBytesPerInputByte = 32;
    private const int RepairDictionaryWindow = 4096;
    private const long ObjectCacheBytes = 8L * 1024 * 1024;
    private const long ObjectStreamCacheBytes = 16L * 1024 * 1024;

    private sealed class ObjectStreamLayout
    {
        private readonly int[] _numbers;
        private readonly int[] _offsetsByNumber;
        private readonly int[] _ascendingOffsets;

        public ObjectStreamLayout(byte[] data, int first, int[] numbers, int[] offsetsByNumber, int[] ascendingOffsets)
        {
            Data = data;
            First = first;
            _numbers = numbers;
            _offsetsByNumber = offsetsByNumber;
            _ascendingOffsets = ascendingOffsets;
        }

        public byte[] Data { get; }
        public int First { get; }
        public PdfSyntaxParser? SummaryParser { get; set; }
        public IReadOnlyList<int> Numbers => _numbers;
        public long RetainedBytes => Data.Length + 4L * (_numbers.Length + _offsetsByNumber.Length + _ascendingOffsets.Length);

        public bool TryGetOffset(int number, out int offset)
        {
            int index = Array.BinarySearch(_numbers, number);
            offset = index >= 0 ? _offsetsByNumber[index] : 0;
            return index >= 0;
        }

        public int EndOf(int offset)
        {
            int index = Array.BinarySearch(_ascendingOffsets, offset + 1);
            if (index < 0) index = ~index;
            return index < _ascendingOffsets.Length ? First + _ascendingOffsets[index] : Data.Length;
        }
    }

    private readonly byte[] _data;
    private readonly PdfObjectCache<PdfObject> _cache = new(ObjectCacheBytes);
    private readonly PdfObjectCache<ObjectStreamLayout> _objectStreams = new(ObjectStreamCacheBytes);
    private readonly HashSet<int> _resolving = new();
    private readonly Func<PdfReference, PdfObject?> _resolveLength;
    private readonly Func<int, int> _findEndstream;
    private PdfSyntaxParser? _fileSummaryParser;
    private PdfCrossReference? _crossReference;
    private Dictionary<int, PdfCrossReferenceEntry>? _repairedEntries;
    private Dictionary<int, long>? _scannedOffsets;
    private long[]? _scannedOffsetsAscending;
    private int[]? _endstreamOffsets;
    private PdfParseBudget _parseBudget;
    private readonly long _decodedBytesLimit;
    private long _decodedBytes;
    private long _entryBudget;
    private int? _highestObjectNumber;

    private PdfObjectStore(byte[] data)
    {
        _data = data;
        _resolveLength = ResolveLength;
        _findEndstream = NextEndstream;
        _entryBudget = Math.Max(MinCrossReferenceEntryBudget, data.Length);
        _parseBudget = new PdfParseBudget(data.Length);
        _decodedBytesLimit = Math.Min(MaxDecodedBytesPerOperation, DecodedBytesAllowance + DecodedBytesPerInputByte * (long)data.Length);
    }

    public PdfDictionary Trailer { get; } = new();

    public long LastCrossReferenceOffset { get; private set; } = -1;

    public bool LastCrossReferenceIsStream { get; private set; }

    public bool WasRepaired => _repairedEntries != null;

    public int HighestObjectNumber => _highestObjectNumber ??= ComputeHighestObjectNumber();

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

    public void BeginOperation()
    {
        _parseBudget = new PdfParseBudget(_data.Length);
        _decodedBytes = 0;
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
        if (_cache.TryGet(number, out var cached)) return cached;
        if (!TryGetEntry(number, out var entry) || entry.Kind == PdfCrossReferenceEntryKind.Free) return PdfNull.Instance;
        if (_resolving.Count >= MaxNestedReads) throw new PdfFormatException("Objects depend on each other too deeply.");
        RuntimeHelpers.EnsureSufficientExecutionStack();
        if (!_resolving.Add(number)) throw new PdfFormatException("An object refers to itself while being read.");
        try
        {
            var value = entry.Kind == PdfCrossReferenceEntryKind.InFile
                ? ReadObjectAt(entry.Offset, number)
                : ReadFromObjectStream(entry.ObjectStream, number);
            _cache.Add(number, value, PdfParseBudget.RetainedBytes(value));
            return value;
        }
        finally
        {
            _resolving.Remove(number);
        }
    }

    public PdfPageTreeSummary? PageTreeSummary(int number)
    {
        if (_cache.TryGet(number, out var cached)) return cached is PdfDictionary dictionary ? PdfPageTreeSummary.Of(dictionary) : null;
        if (!TryGetEntry(number, out var entry) || entry.Kind == PdfCrossReferenceEntryKind.Free) return null;
        if (entry.Kind == PdfCrossReferenceEntryKind.InFile)
        {
            if (entry.Offset >= 0 && entry.Offset < _data.Length)
            {
                int start = (int)entry.Offset;
                var parser = (_fileSummaryParser ??= NewParser(start)).MoveTo(start, ParseEnd(start), _parseBudget);
                if (parser.TryReadIndirectHeader(out int found, out _) && found == number) return parser.ReadPageTreeSummary();
            }
            return Get(number) is PdfDictionary scanned ? PdfPageTreeSummary.Of(scanned) : null;
        }
        if (IsEncrypted) throw new NotSupportedException("Objects inside an encrypted PDF's object streams can't be read.");
        var layout = ObjectStreamOffsets(entry.ObjectStream);
        if (!layout.TryGetOffset(number, out int offset)) return null;
        layout.SummaryParser ??= new PdfSyntaxParser(layout.Data, 0, layout.Data.Length);
        return layout.SummaryParser.MoveTo(layout.First + offset, layout.EndOf(offset), _parseBudget).ReadPageTreeSummary();
    }

    private bool TryGetEntry(int number, out PdfCrossReferenceEntry entry)
    {
        if (_repairedEntries != null) return _repairedEntries.TryGetValue(number, out entry);
        if (_crossReference != null) return _crossReference.TryFind(number, out entry);
        entry = default;
        return false;
    }

    private int ComputeHighestObjectNumber()
    {
        if (_repairedEntries == null) return _crossReference?.HighestObjectNumber ?? 0;
        int highest = 0;
        foreach (var number in _repairedEntries.Keys) highest = Math.Max(highest, number);
        return highest;
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

    private void RebuildFromObjectScan()
    {
        _repairedEntries = new Dictionary<int, PdfCrossReferenceEntry>();
        var scanned = ScanObjectOffsets();
        foreach (var pair in scanned)
            _repairedEntries[pair.Key] = new PdfCrossReferenceEntry(PdfCrossReferenceEntryKind.InFile, pair.Value, 0);

        int trailerEnd = _data.Length;
        for (int at = PdfSyntaxParser.LastIndexOf(_data, "trailer", _data.Length); at >= 0;
             trailerEnd = at, at = PdfSyntaxParser.LastIndexOf(_data, "trailer", at))
        {
            try
            {
                var parser = new PdfSyntaxParser(_data, at + "trailer".Length, trailerEnd, budget: _parseBudget);
                if (parser.ReadObject() is PdfDictionary trailer) MergeTrailer(trailer);
            }
            catch (PdfFormatException ex) when (ex is not PdfParseBudgetExceededException)
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
        catch (PdfFormatException ex) when (ex is not PdfParseBudgetExceededException)
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
            foreach (var number in ObjectStreamOffsets(streamNumber).Numbers)
                if (!_repairedEntries!.ContainsKey(number))
                    _repairedEntries[number] = new PdfCrossReferenceEntry(PdfCrossReferenceEntryKind.InObjectStream, 0, streamNumber);
        }
        catch (PdfFormatException ex) when (ex is not PdfParseBudgetExceededException)
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
            if (parser.TryReadIndirectHeader(out int number, out _) && number <= PdfCrossReference.MaxObjectNumber)
                offsets[number] = i;
        }
        _scannedOffsetsAscending = new long[offsets.Count];
        offsets.Values.CopyTo(_scannedOffsetsAscending, 0);
        Array.Sort(_scannedOffsetsAscending);
        return _scannedOffsets = offsets;
    }

    private int ParseEnd(long objectOffset)
    {
        if (_repairedEntries == null || _scannedOffsetsAscending == null) return _data.Length;
        int index = Array.BinarySearch(_scannedOffsetsAscending, objectOffset + 1);
        if (index < 0) index = ~index;
        return index < _scannedOffsetsAscending.Length ? (int)_scannedOffsetsAscending[index] : _data.Length;
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

    private PdfSyntaxParser NewParser(int start) => new(_data, start, ParseEnd(start), _resolveLength, _findEndstream, _parseBudget);

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
        SpendDecodeBudget(stream.DataLength);
        var decoded = PdfStreamDecoder.Decode(stream);
        SpendDecodeBudget(decoded.Length);
        _parseBudget.AllowScanningDecoded(decoded.Length);
        return decoded;
    }

    private void SpendDecodeBudget(long bytes)
    {
        _decodedBytes += bytes;
        if (_decodedBytes > _decodedBytesLimit)
            throw new PdfParseBudgetExceededException("The document's compressed structure expands far beyond its size.");
    }

    private PdfObject ReadFromObjectStream(int streamNumber, int number)
    {
        if (IsEncrypted) throw new NotSupportedException("Objects inside an encrypted PDF's object streams can't be read.");
        var layout = ObjectStreamOffsets(streamNumber);
        if (!layout.TryGetOffset(number, out int offset)) return PdfNull.Instance;
        return new PdfSyntaxParser(layout.Data, layout.First + offset, layout.EndOf(offset), budget: _parseBudget).ReadObject();
    }

    private ObjectStreamLayout ObjectStreamOffsets(int streamNumber)
    {
        if (_objectStreams.TryGet(streamNumber, out var layout)) return layout;
        if (TryGetEntry(streamNumber, out var entry) && entry.Kind == PdfCrossReferenceEntryKind.InObjectStream)
            throw new PdfFormatException("An object stream is stored inside another object stream.");
        if (Get(streamNumber) is not PdfStream stream || stream.Dictionary.Get("Type") is not PdfName { Value: "ObjStm" })
            throw new PdfFormatException("A compressed object points at something that isn't an object stream.");

        int count = PdfStreamDecoder.IntegerOr(stream.Dictionary.Get("N"), -1);
        int first = PdfStreamDecoder.IntegerOr(stream.Dictionary.Get("First"), -1);
        var data = Decode(stream);
        if (count < 0 || count > MaxObjectsPerObjectStream || first < 0 || first > data.Length)
            throw new PdfFormatException("An object stream has an invalid /N or /First.");

        int expected = Math.Min(count, first / 4 + 1);
        var keys = new List<long>(expected);
        var ascending = new List<int>(expected);
        var header = new PdfSyntaxParser(data, 0, first, budget: _parseBudget);
        long previousOffset = -1;
        for (int i = 0; i < count; i++)
        {
            long number = header.ReadInteger();
            long offset = header.ReadInteger();
            if (number <= 0 || number > PdfCrossReference.MaxObjectNumber || offset <= previousOffset || first + offset >= data.Length) continue;
            previousOffset = offset;
            keys.Add((number << 32) | (uint)ascending.Count);
            ascending.Add((int)offset);
        }
        layout = Layout(data, first, keys, ascending.ToArray());
        _objectStreams.Add(streamNumber, layout, layout.RetainedBytes);
        return layout;
    }

    private static ObjectStreamLayout Layout(byte[] data, int first, List<long> keys, int[] ascendingOffsets)
    {
        var sorted = keys.ToArray();
        Array.Sort(sorted);
        var numbers = new List<int>(sorted.Length);
        var offsets = new List<int>(sorted.Length);
        foreach (long key in sorted)
        {
            int number = (int)(key >> 32);
            if (numbers.Count > 0 && numbers[numbers.Count - 1] == number) continue;
            numbers.Add(number);
            offsets.Add(ascendingOffsets[(int)(key & 0xFFFFFFFF)]);
        }
        return new ObjectStreamLayout(data, first, numbers.ToArray(), offsets.ToArray(), ascendingOffsets);
    }
}
