using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace EggPdf.Pdf;

internal sealed partial class PdfObjectStore : IPdfStreamLocator
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

    private const int InitialWindowBytes = 4 * 1024;
    private const int MaxWindowBytes = 64 * 1024 * 1024;
    private const int EndstreamWindowBytes = 64;
    private const int ScanChunkBytes = 1024 * 1024;
    private const int ScanOverlapBytes = 64;
    private const int TrailerWindowBytes = 1024 * 1024;
    private const int MaxIndexedKeywords = 8_000_000;
    private const int DefaultCertificationPermission = 2;
    private const int CutTokenBytes = 64;

    private readonly PdfByteSource _source;
    private readonly long _length;
    private readonly PdfObjectCache<PdfObject> _cache = new(ObjectCacheBytes);
    private readonly PdfObjectCache<ObjectStreamLayout> _objectStreams = new(ObjectStreamCacheBytes, alwaysKept: 2);
    private readonly HashSet<int> _resolving = new();
    private readonly Func<PdfReference, PdfObject?> _resolveLength;
    private PdfSyntaxParser? _fileSummaryParser;
    private PdfCrossReference? _crossReference;
    private Dictionary<int, PdfCrossReferenceEntry>? _repairedEntries;
    private Dictionary<int, long>? _scannedOffsets;
    private long[]? _scannedOffsetsAscending;
    private long[]? _endstreamOffsets;
    private PdfParseBudget _parseBudget;
    private readonly long _decodedBytesLimit;
    private long _decodedBytes;
    private long _entryBudget;
    private int? _highestObjectNumber;

    private PdfObjectStore(PdfByteSource source)
    {
        _source = source;
        _length = source.Length;
        _resolveLength = ResolveLength;
        _entryBudget = Math.Max(MinCrossReferenceEntryBudget, _length);
        _parseBudget = new PdfParseBudget(_length);
        _decodedBytesLimit = Math.Min(MaxDecodedBytesPerOperation, DecodedBytesAllowance + DecodedBytesPerInputByte * _length);
    }

    public PdfByteSource Source => _source;

    public PdfDictionary Trailer { get; } = new();

    public long LastCrossReferenceOffset { get; private set; } = -1;

    public bool LastCrossReferenceIsStream { get; private set; }

    public bool WasRepaired => _repairedEntries != null;

    public int HighestObjectNumber => _highestObjectNumber ??= ComputeHighestObjectNumber();

    public bool IsEncrypted => Trailer.Get("Encrypt") is { } encrypt && encrypt is not PdfNull;

    public int? CertificationPermission
    {
        get
        {
            if (Resolve(FindCatalog()?.Get("Perms")) is not PdfDictionary permissions || permissions.Get("DocMDP") == null) return null;
            var signature = Resolve(permissions.Get("DocMDP")) as PdfDictionary;
            if (Resolve(signature?.Get("Reference")) is PdfArray references)
                foreach (var item in references.Items)
                    if (Resolve(item) is PdfDictionary reference &&
                        Resolve(reference.Get("TransformMethod")) is PdfName { Value: "DocMDP" } &&
                        Resolve(reference.Get("TransformParams")) is PdfDictionary parameters &&
                        Resolve(parameters.Get("P")) is PdfNumber { IsInteger: true } level)
                        return (int)level.Value;
            return DefaultCertificationPermission;
        }
    }

    public static PdfObjectStore Load(PdfByteSource source)
    {
        var store = new PdfObjectStore(source);
        if (!store.TryLoadCrossReferences() || !store.HasReadableCatalog())
        {
            store = new PdfObjectStore(source);
            store.RebuildFromObjectScan();
        }
        if (store.FindCatalog() == null) throw new PdfFormatException("No document catalog found: this isn't a readable PDF.");
        return store;
    }

    public void ResetCallBudgets()
    {
        _parseBudget = new PdfParseBudget(_length);
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
            _cache.Add(number, value, PdfObjectSize.Of(value));
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
            if (entry.Offset >= 0 && entry.Offset < _length)
            {
                var summary = SummaryAt(entry.Offset, number, out bool found);
                if (found) return summary;
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

        var trailers = _source.FindAll("trailer", MaxIndexedKeywords);
        for (int t = trailers.Length - 1; t >= 0; t--)
        {
            long trailerEnd = t + 1 < trailers.Length ? trailers[t + 1] : _length;
            try
            {
                long start = trailers[t] + "trailer".Length;
                var window = _source.Window(start, (int)Math.Min(TrailerWindowBytes, trailerEnd - start));
                var parser = new PdfSyntaxParser(window.Data, window.Offset, window.End, budget: _parseBudget, baseOffset: window.BaseOffset);
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
            long start = byOffset[i].Value;
            long end = Math.Min(i + 1 < byOffset.Count ? byOffset[i + 1].Value : _length, start + RepairDictionaryWindow);
            var window = _source.Window(start, (int)(end - start));
            int windowEnd = window.Offset + (int)(end - start);
            bool catalog = PdfSyntaxParser.IndexOf(window.Data, "/Catalog", window.Offset, windowEnd) >= 0;
            bool objectStream = PdfSyntaxParser.IndexOf(window.Data, "/ObjStm", window.Offset, windowEnd) >= 0;
            bool crossReference = PdfSyntaxParser.IndexOf(window.Data, "/XRef", window.Offset, windowEnd) >= 0;
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
        const int span = ScanChunkBytes + ScanOverlapBytes + 1;
        byte[]? buffer = _source.IsInMemory ? null : new byte[(int)Math.Min(span, _length)];
        for (long chunkStart = 0; chunkStart < _length; chunkStart += ScanChunkBytes)
        {
            long readStart = Math.Max(0, chunkStart - 1);
            int length = (int)Math.Min(span, _length - readStart);
            var window = buffer == null ? _source.Window(readStart, length) : ReadInto(buffer, readStart, length);
            var data = window.Data;
            int first = window.Offset + (int)(chunkStart - readStart);
            int last = Math.Min(window.End, first + ScanChunkBytes);
            for (int i = first; i < last; i++)
            {
                if (window.BaseOffset + i > 0 && data[i - 1] != '\n' && data[i - 1] != '\r') continue;
                if (data[i] < '0' || data[i] > '9') continue;
                var parser = new PdfSyntaxParser(data, i, window.End);
                if (parser.TryReadIndirectHeader(out int number, out _) && number <= PdfCrossReference.MaxObjectNumber)
                    offsets[number] = window.BaseOffset + i;
            }
        }
        _scannedOffsetsAscending = new long[offsets.Count];
        offsets.Values.CopyTo(_scannedOffsetsAscending, 0);
        Array.Sort(_scannedOffsetsAscending);
        return _scannedOffsets = offsets;
    }

    private PdfByteWindow ReadInto(byte[] buffer, long start, int length)
    {
        _source.Read(start, buffer, length);
        return new PdfByteWindow(buffer, 0, start, length);
    }

    private long ParseEnd(long objectOffset)
    {
        if (_repairedEntries == null || _scannedOffsetsAscending == null) return _length;
        int index = Array.BinarySearch(_scannedOffsetsAscending, objectOffset + 1);
        if (index < 0) index = ~index;
        return index < _scannedOffsetsAscending.Length ? _scannedOffsetsAscending[index] : _length;
    }

    private long NextEndstream(long from)
    {
        _endstreamOffsets ??= _source.FindAll("endstream", MaxIndexedKeywords);
        int index = Array.BinarySearch(_endstreamOffsets, from);
        if (index < 0) index = ~index;
        return index < _endstreamOffsets.Length ? _endstreamOffsets[index] : -1;
    }

    public bool EndstreamAt(long position)
    {
        var window = _source.Window(position, EndstreamWindowBytes);
        return new PdfSyntaxParser(window.Data, window.Offset, window.End).TryReadKeyword("endstream");
    }

    public bool TryFindDataEnd(long dataStart, long end, out long dataLength)
    {
        dataLength = 0;
        long endstream = NextEndstream(dataStart);
        if (endstream < 0 || endstream + "endstream".Length > end) return false;
        long dataEnd = endstream;
        long tailStart = Math.Max(dataStart, endstream - 2);
        var tail = _source.Window(tailStart, (int)(endstream - tailStart));
        int last = tail.Offset + (int)(endstream - tailStart) - 1;
        if (dataEnd > dataStart && tail.Data[last] == '\n')
        {
            dataEnd--;
            last--;
        }
        if (dataEnd > dataStart && last >= tail.Offset && tail.Data[last] == '\r') dataEnd--;
        dataLength = dataEnd - dataStart;
        return true;
    }

    private PdfSyntaxParser NewParser(long start, int windowBytes = InitialWindowBytes)
    {
        long end = ParseEnd(start);
        int length = (int)Math.Max(0, Math.Min(_source.IsInMemory ? int.MaxValue : windowBytes, end - start));
        var window = _source.Window(start, length);
        return new PdfSyntaxParser(window.Data, window.Offset, window.End, _resolveLength, this, _parseBudget, window.BaseOffset, end);
    }

    private T ParseAt<T>(long start, Func<PdfSyntaxParser, T> read)
    {
        for (int windowBytes = InitialWindowBytes; ; windowBytes = (int)Math.Min(8L * windowBytes, MaxWindowBytes))
        {
            var parser = NewParser(start, windowBytes);
            bool canGrow = parser.EndsBeforeObject && windowBytes < MaxWindowBytes;
            T result;
            try
            {
                result = read(parser);
            }
            catch (PdfWindowTooSmallException) when (canGrow)
            {
                continue;
            }
            catch (PdfFormatException ex) when (ex is not PdfParseBudgetExceededException && canGrow &&
                                                parser.Position >= parser.End - CutTokenBytes)
            {
                continue;
            }
            if (canGrow && parser.ReachedWindowEnd) continue;
            return result;
        }
    }

    private PdfObject ReadObjectAt(long offset, int expectedNumber)
    {
        if (offset >= 0 && offset < _length && TryParseIndirectObject(offset, expectedNumber, out var value)) return value!;
        if (ScanObjectOffsets().TryGetValue(expectedNumber, out var scanned) && scanned != offset &&
            TryParseIndirectObject(scanned, expectedNumber, out value)) return value!;
        throw new PdfFormatException($"Object {expectedNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)} isn't where the cross-reference says.");
    }

    private bool TryParseIndirectObject(long offset, int expectedNumber, out PdfObject? value)
    {
        value = ParseAt(offset, parser =>
            parser.TryReadIndirectHeader(out int number, out _) && number == expectedNumber ? parser.ReadIndirectObjectBody() : null);
        return value != null;
    }

    private PdfPageTreeSummary? SummaryAt(long offset, int number, out bool found)
    {
        if (!_source.IsInMemory) return SummaryFromStream(offset, number, out found);
        var parser = (_fileSummaryParser ??= NewParser(offset)).MoveTo((int)offset, (int)ParseEnd(offset), _parseBudget);
        found = parser.TryReadIndirectHeader(out int header, out _) && header == number;
        return found ? parser.ReadPageTreeSummary() : null;
    }

    private PdfPageTreeSummary? SummaryFromStream(long offset, int number, out bool found)
    {
        var result = ParseAt<(PdfPageTreeSummary? Summary, bool Found)>(offset, parser =>
            parser.TryReadIndirectHeader(out int header, out _) && header == number ? (parser.ReadPageTreeSummary(), true) : (null, false));
        found = result.Found;
        return result.Summary;
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
