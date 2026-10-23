using System;
using System.Collections.Generic;

namespace EggPdf.Pdf;

internal sealed partial class PdfPageTree
{
    public const int MaxPages = 1_000_000;
    private const int MaxDepth = 64;
    private const int MaxIndexedKids = 4_000_000;
    private const int DirectKid = -1;

    private sealed class NodeIndex
    {
        public NodeIndex(int[] pagesThroughKid, long[] kidReferences)
        {
            PagesThroughKid = pagesThroughKid;
            KidReferences = kidReferences;
        }

        public int[] PagesThroughKid { get; }
        public long[] KidReferences { get; }
        public int Total => PagesThroughKid.Length == 0 ? 0 : PagesThroughKid[PagesThroughKid.Length - 1];

        public int KidHolding(int page)
        {
            int index = Array.BinarySearch(PagesThroughKid, page);
            if (index < 0) return ~index;
            while (index > 0 && PagesThroughKid[index - 1] == page) index--;
            return index;
        }
    }

    private readonly PdfObjectStore _store;
    private readonly PdfReference? _rootReference;
    private readonly PdfDictionary? _directRoot;
    private readonly InheritedAttributes _rootAttributes;
    private readonly bool _rootIsPage;
    private readonly Dictionary<int, NodeIndex> _indexes = new();
    private readonly HashSet<int> _indexing = new();
    private long _indexedKids;
    private WalkedPages? _walked;

    public PdfPageTree(PdfObjectStore store)
    {
        _store = store;
        var catalog = store.FindCatalog()!;
        _rootReference = catalog.Get("Pages") as PdfReference;
        var root = store.Resolve(catalog.Get("Pages")) as PdfDictionary
            ?? throw new PdfFormatException("The document catalog has no page tree.");
        _directRoot = _rootReference == null ? root : null;
        _rootAttributes = InheritedAttributes.Root.Inherit(store, root);
        _rootIsPage = IsLeaf(root);

        if (_rootIsPage)
        {
            Count = 1;
            return;
        }
        Count = DeclaredCount(root.Get("Count")) ?? Index(_rootReference, root, 0).Total;
        if (Count == 0) return;
        try
        {
            Find(Count);
        }
        catch (PdfFormatException ex) when (ex is not PdfParseBudgetExceededException)
        {
            _walked = WalkedPages.Walk(this);
            Count = _walked.Count;
        }
    }

    public int Count { get; }

    public PdfPageNode Find(int pageNumber)
    {
        if (_walked != null) return _walked.Find(this, pageNumber);
        if (_rootIsPage) return PageNode(pageNumber, Root(), _rootReference, _rootAttributes);

        var attributes = _rootAttributes;
        var node = _directRoot;
        var reference = _rootReference;
        int expected = Count;
        int remaining = pageNumber;
        var path = new List<int>();
        for (int depth = 0; ; depth++)
        {
            if (depth > MaxDepth) throw new PdfFormatException("The page tree is nested too deeply.");
            if (reference != null)
            {
                if (path.Contains(reference.ObjectNumber)) throw new PdfFormatException("The page tree contains a cycle.");
                path.Add(reference.ObjectNumber);
            }

            var index = Index(reference, node, depth);
            if (index.Total != expected)
                throw new PdfFormatException("The page tree is inconsistent: a /Pages node's /Count doesn't match the pages under it.");

            int kid = index.KidHolding(remaining);
            int before = kid > 0 ? index.PagesThroughKid[kid - 1] : 0;
            remaining -= before;
            var (kidDictionary, kidReference) = Kid(reference, node, index, kid);
            attributes = attributes.Inherit(_store, kidDictionary);
            expected = index.PagesThroughKid[kid] - before;
            if (IsLeaf(kidDictionary))
            {
                if (expected != 1) throw new PdfFormatException("The page tree is inconsistent: a page sits where a /Pages node was counted.");
                return PageNode(pageNumber, kidDictionary, kidReference, attributes);
            }

            node = kidDictionary;
            reference = kidReference;
        }
    }

    private PdfDictionary Root() => _directRoot ?? Node(_rootReference);

    private PdfDictionary Node(PdfReference? reference) =>
        _store.Resolve(reference) as PdfDictionary ?? throw new PdfFormatException("A page tree node changed while it was being read.");

    private NodeIndex Index(PdfReference? reference, PdfDictionary? node, int depth)
    {
        if (reference != null && _indexes.TryGetValue(reference.ObjectNumber, out var cached)) return cached;
        if (depth > MaxDepth) throw new PdfFormatException("The page tree is nested too deeply.");
        if (reference != null && !_indexing.Add(reference.ObjectNumber)) throw new PdfFormatException("The page tree contains a cycle.");
        try
        {
            var kids = _store.Resolve((node ?? Node(reference)).Get("Kids")) as PdfArray;
            int count = kids?.Items.Count ?? 0;
            var pagesThroughKid = new int[count];
            var kidReferences = new long[count];
            long total = 0;
            for (int i = 0; i < count; i++)
            {
                var item = kids!.Items[i];
                var kidReference = item as PdfReference;
                kidReferences[i] = kidReference != null ? Pack(kidReference) : DirectKid;
                if (Summarize(item) is { } kid) total += PagesIn(kid, item, depth + 1);
                if (total > MaxPages) throw new PdfFormatException("The document has too many pages.");
                pagesThroughKid[i] = (int)total;
            }

            var index = new NodeIndex(pagesThroughKid, kidReferences);
            if (reference != null)
            {
                if (_indexedKids + count > MaxIndexedKids)
                {
                    _indexes.Clear();
                    _indexedKids = 0;
                }
                _indexes[reference.ObjectNumber] = index;
                _indexedKids += count;
            }
            return index;
        }
        finally
        {
            if (reference != null) _indexing.Remove(reference.ObjectNumber);
        }
    }

    private PdfPageTreeSummary? Summarize(PdfObject item) => item is PdfReference reference
        ? _store.PageTreeSummary(reference.ObjectNumber)
        : _store.Resolve(item) is PdfDictionary direct ? PdfPageTreeSummary.Of(direct) : null;

    private int PagesIn(PdfPageTreeSummary kid, PdfObject item, int depth)
    {
        if (IsLeaf(kid)) return 1;
        if (_store.Resolve(kid.Kids) is not PdfArray) return 0;
        if (DeclaredCount(kid.Count) is int declared) return declared;
        if (_store.Resolve(item) is not PdfDictionary node) return 0;
        return Index(item as PdfReference, node, depth).Total;
    }

    private (PdfDictionary Dictionary, PdfReference? Reference) Kid(PdfReference? nodeReference, PdfDictionary? node, NodeIndex index, int kid)
    {
        long packed = index.KidReferences[kid];
        if (packed != DirectKid)
        {
            var reference = Unpack(packed);
            if (_store.Resolve(reference) is PdfDictionary byReference) return (byReference, reference);
        }
        else if (_store.Resolve((node ?? Node(nodeReference)).Get("Kids")) is PdfArray kids && kid < kids.Items.Count &&
                 _store.Resolve(kids.Items[kid]) is PdfDictionary direct)
        {
            return (direct, null);
        }
        throw new PdfFormatException("A page tree node changed while it was being read.");
    }

    private int? DeclaredCount(PdfObject? count)
    {
        if (_store.Resolve(count) is not PdfNumber number || !number.IsInteger || number.Value < 1) return null;
        if (number.Value > Math.Max(1, _store.HighestObjectNumber)) return null;
        if (number.Value > MaxPages) throw new PdfFormatException("The document has too many pages.");
        return (int)number.Value;
    }

    private bool IsLeaf(PdfDictionary node) => IsLeaf(PdfPageTreeSummary.Of(node));

    private bool IsLeaf(PdfPageTreeSummary node) => node.Type switch
    {
        PdfPageTreeNodeType.Page => true,
        PdfPageTreeNodeType.Pages => false,
        _ => _store.Resolve(node.Kids) is not PdfArray,
    };

    private static long Pack(PdfReference reference) => ((long)reference.ObjectNumber << 16) | (uint)(reference.Generation & 0xFFFF);

    private static PdfReference Unpack(long packed) => new((int)(packed >> 16), (int)(packed & 0xFFFF));

    private static PdfPageNode PageNode(int pageNumber, PdfDictionary dictionary, PdfReference? reference, InheritedAttributes attributes)
    {
        var media = attributes.MediaBox ?? new[] { 0.0, 0.0, 612.0, 792.0 };
        var visible = attributes.CropBox == null ? media : Intersect(attributes.CropBox, media) ?? media;
        double width = visible[2] - visible[0];
        double height = visible[3] - visible[1];
        int rotation = attributes.Rotation;
        var geometry = rotation % 180 == 0
            ? new PdfPageGeometry(pageNumber, width, height, rotation)
            : new PdfPageGeometry(pageNumber, height, width, rotation);
        return new PdfPageNode(reference, dictionary, visible, rotation, attributes.Resources, geometry);
    }

    private static double[]? Intersect(double[] a, double[] b)
    {
        var result = new[] { Math.Max(a[0], b[0]), Math.Max(a[1], b[1]), Math.Min(a[2], b[2]), Math.Min(a[3], b[3]) };
        return result[2] > result[0] && result[3] > result[1] ? result : null;
    }
}
