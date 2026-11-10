using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Writes a merged PDF: the pages of each input with every object they reach (resources, fonts, images,
/// annotations) under new object numbers, one page tree and catalog, and a classic cross-reference table.
/// Objects are written as soon as they are copied, and stream data is copied in bounded chunks, so memory grows
/// with the number of objects, not with their size.
/// </summary>
internal sealed class PdfMergeWriter
{
    private const int CopyBufferSize = 64 * 1024;
    private const int MaxParentDepth = 64;
    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
    private static readonly string[] InheritedBoxes = { "MediaBox", "CropBox" };
    private static readonly HashSet<string> DroppedPageKeys = new(StringComparer.Ordinal) { "Parent", "StructParents", "B" };

    private readonly Stream _output;
    private readonly List<long> _offsets = new() { -1 };
    private readonly List<int> _pageObjects = new();
    private readonly int _pagesObject;
    private long _position;
    private byte[]? _copyBuffer;

    public PdfMergeWriter(Stream output)
    {
        _output = output;
        WriteText("%PDF-1.7\n");
        WriteBytes(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });
        _pagesObject = Reserve();
    }

    /// <summary>Copies every page of <paramref name="reader"/> with the objects it reaches.</summary>
    public void AddPages(PdfReader reader)
    {
        var copy = new InputCopy(this, reader.Store);
        var newPageNumbers = new int[reader.PageCount];
        for (int n = 1; n <= reader.PageCount; n++)
        {
            int number = Reserve();
            newPageNumbers[n - 1] = number;
            var reference = reader.GetPageNode(n).Reference;
            if (reference != null) copy.Map(reference.ObjectNumber, number);
        }

        for (int n = 1; n <= reader.PageCount; n++)
        {
            int number = newPageNumbers[n - 1];
            var page = copy.CopyPage(reader.GetPageNode(n));
            page.Set("Parent", new PdfReference(_pagesObject, 0));
            WriteObject(number, page);
            _pageObjects.Add(number);
            copy.WritePending();
        }
    }

    /// <summary>Writes the page tree, the catalog, the cross-reference table and the trailer.</summary>
    public void Finish()
    {
        var kids = new PdfArray();
        foreach (int page in _pageObjects) kids.Items.Add(new PdfReference(page, 0));
        var pages = new PdfDictionary();
        pages.Set("Type", new PdfName("Pages"));
        pages.Set("Kids", kids);
        pages.Set("Count", new PdfNumber(_pageObjects.Count));
        WriteObject(_pagesObject, pages);

        int catalogNumber = Reserve();
        var catalog = new PdfDictionary();
        catalog.Set("Type", new PdfName("Catalog"));
        catalog.Set("Pages", new PdfReference(_pagesObject, 0));
        WriteObject(catalogNumber, catalog);

        long crossReferenceOffset = _position;
        var table = new StringBuilder();
        table.Append("xref\n0 ").Append(_offsets.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        table.Append("0000000000 65535 f \n");
        for (int i = 1; i < _offsets.Count; i++)
        {
            table.Append(_offsets[i] < 0
                ? "0000000000 65535 f \n"
                : _offsets[i].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }
        WriteText(table.ToString());

        var id = PdfObjectSerializer.Serialize(new PdfString(Guid.NewGuid().ToByteArray()));
        WriteText("trailer\n<< /Size " + _offsets.Count.ToString(CultureInfo.InvariantCulture) +
                  " /Root " + catalogNumber.ToString(CultureInfo.InvariantCulture) + " 0 R /ID [" + id + " " + id + "] >>\n" +
                  "startxref\n" + crossReferenceOffset.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
        _output.Flush();
    }

    private int Reserve()
    {
        _offsets.Add(-1);
        return _offsets.Count - 1;
    }

    private void WriteObject(int number, PdfObject value)
    {
        _offsets[number] = _position;
        WriteText(number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n" + PdfObjectSerializer.Serialize(value) + "\nendobj\n");
    }

    private void WriteStreamObject(int number, PdfDictionary dictionary, PdfStream source)
    {
        _offsets[number] = _position;
        WriteText(number.ToString(CultureInfo.InvariantCulture) + " 0 obj\n" + PdfObjectSerializer.Serialize(dictionary) + "\nstream\n");
        _copyBuffer ??= new byte[CopyBufferSize];
        long position = source.DataStart;
        long remaining = source.DataLength;
        while (remaining > 0)
        {
            int chunk = (int)Math.Min(remaining, _copyBuffer.Length);
            source.Source.Read(position, _copyBuffer, chunk);
            WriteBytes(_copyBuffer, chunk);
            position += chunk;
            remaining -= chunk;
        }
        WriteText("\nendstream\nendobj\n");
    }

    private void WriteText(string text) => WriteBytes(Latin1.GetBytes(text));

    private void WriteBytes(byte[] bytes) => WriteBytes(bytes, bytes.Length);

    private void WriteBytes(byte[] bytes, int count)
    {
        _output.Write(bytes, 0, count);
        _position += count;
    }

    /// <summary>
    /// The copy of one input: its object numbers mapped to new ones, and the objects reached but not yet written.
    /// A reference to the input's page tree or catalog (reached through links or form fields) is dropped.
    /// </summary>
    private sealed class InputCopy
    {
        private const int Dropped = -1;

        private readonly PdfMergeWriter _writer;
        private readonly PdfObjectStore _store;
        private readonly Dictionary<int, int> _numbers = new();
        private readonly Queue<(int Number, PdfObject Value)> _pending = new();

        public InputCopy(PdfMergeWriter writer, PdfObjectStore store)
        {
            _writer = writer;
            _store = store;
        }

        public void Map(int sourceNumber, int newNumber) => _numbers[sourceNumber] = newNumber;

        public PdfDictionary CopyPage(PdfPageNode node)
        {
            var source = node.Dictionary;
            var page = new PdfDictionary();
            foreach (var entry in source.Entries)
            {
                if (DroppedPageKeys.Contains(entry.Key)) continue;
                page.Set(entry.Key, Copy(entry.Value));
            }
            page.Set("Type", new PdfName("Page"));

            if (page.Get("Resources") == null && node.Resources != null)
                page.Set("Resources", Copy(node.Resources));
            foreach (var box in InheritedBoxes)
            {
                if (page.Get(box) != null) continue;
                var inherited = Inherited(source, box);
                if (inherited != null) page.Set(box, Copy(inherited));
            }
            if (page.Get("MediaBox") == null)
                page.Set("MediaBox", Box(node.VisibleBox));
            if (page.Get("Rotate") == null && node.Rotation != 0)
                page.Set("Rotate", new PdfNumber(node.Rotation));
            return page;
        }

        public void WritePending()
        {
            while (_pending.Count > 0)
            {
                var (number, value) = _pending.Dequeue();
                if (value is PdfStream stream)
                {
                    var dictionary = CopyDictionary(stream.Dictionary);
                    dictionary.Set("Length", new PdfNumber(stream.DataLength));
                    _writer.WriteStreamObject(number, dictionary, stream);
                }
                else
                {
                    _writer.WriteObject(number, Copy(value));
                }
            }
        }

        private PdfObject? Inherited(PdfDictionary page, string key)
        {
            var node = _store.Resolve(page.Get("Parent")) as PdfDictionary;
            for (int depth = 0; node != null && depth < MaxParentDepth; depth++)
            {
                var value = node.Get(key);
                if (value != null) return value;
                node = _store.Resolve(node.Get("Parent")) as PdfDictionary;
            }
            return null;
        }

        private static PdfArray Box(double[] box)
        {
            var array = new PdfArray();
            foreach (var value in box) array.Items.Add(new PdfNumber(value));
            return array;
        }

        private PdfObject Copy(PdfObject value)
        {
            switch (value)
            {
                case PdfReference reference:
                    return CopyReference(reference);
                case PdfArray array:
                    var copiedArray = new PdfArray();
                    foreach (var item in array.Items) copiedArray.Items.Add(Copy(item));
                    return copiedArray;
                case PdfDictionary dictionary:
                    return CopyDictionary(dictionary);
                case PdfStream:
                    return PdfNull.Instance;
                default:
                    return value;
            }
        }

        private PdfDictionary CopyDictionary(PdfDictionary dictionary)
        {
            var copy = new PdfDictionary();
            foreach (var entry in dictionary.Entries) copy.Set(entry.Key, Copy(entry.Value));
            return copy;
        }

        private PdfObject CopyReference(PdfReference reference)
        {
            if (_numbers.TryGetValue(reference.ObjectNumber, out int mapped))
                return mapped == Dropped ? PdfNull.Instance : new PdfReference(mapped, 0);

            PdfObject? target;
            try
            {
                target = _store.Get(reference.ObjectNumber);
            }
            catch (Exception ex) when (ex is PdfFormatException || ex is NotSupportedException)
            {
                target = null;
            }

            var dictionary = target as PdfDictionary ?? (target as PdfStream)?.Dictionary;
            if (target == null || target is PdfNull || (dictionary != null && IsDocumentStructure(dictionary)))
            {
                _numbers[reference.ObjectNumber] = Dropped;
                return PdfNull.Instance;
            }

            int number = _writer.Reserve();
            _numbers[reference.ObjectNumber] = number;
            _pending.Enqueue((number, target));
            return new PdfReference(number, 0);
        }

        private static bool IsDocumentStructure(PdfDictionary dictionary)
            => dictionary.Get("Type") is PdfName type &&
               (type.Value == "Pages" || type.Value == "Page" || type.Value == "Catalog" || type.Value == "StructTreeRoot" || type.Value == "Outlines");
    }
}
