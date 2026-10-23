using System;
using System.Collections.Generic;

namespace EggPdf.Pdf;

internal sealed partial class PdfPageTree
{
    private sealed class InheritedAttributes
    {
        public static readonly InheritedAttributes Root = new(null, null, 0, null);

        private InheritedAttributes(double[]? mediaBox, double[]? cropBox, int rotation, PdfObject? resources)
        {
            MediaBox = mediaBox;
            CropBox = cropBox;
            Rotation = rotation;
            Resources = resources;
        }

        public double[]? MediaBox { get; }
        public double[]? CropBox { get; }
        public int Rotation { get; }
        public PdfObject? Resources { get; }

        public InheritedAttributes Inherit(PdfObjectStore store, PdfDictionary node) => new(
            ReadRectangle(store, node.Get("MediaBox")) ?? MediaBox,
            ReadRectangle(store, node.Get("CropBox")) ?? CropBox,
            store.Resolve(node.Get("Rotate")) is PdfNumber rotate ? NormalizeRotation(rotate.Value) : Rotation,
            node.Get("Resources") ?? Resources);

        private static double[]? ReadRectangle(PdfObjectStore store, PdfObject? value)
        {
            if (store.Resolve(value) is not PdfArray array || array.Items.Count != 4) return null;
            var numbers = new double[4];
            for (int i = 0; i < 4; i++)
            {
                if (store.Resolve(array.Items[i]) is not PdfNumber number) return null;
                numbers[i] = number.Value;
            }
            var rectangle = new[]
            {
                Math.Min(numbers[0], numbers[2]), Math.Min(numbers[1], numbers[3]),
                Math.Max(numbers[0], numbers[2]), Math.Max(numbers[1], numbers[3]),
            };
            return rectangle[2] > rectangle[0] && rectangle[3] > rectangle[1] ? rectangle : null;
        }

        private static int NormalizeRotation(double degrees)
        {
            if (double.IsNaN(degrees) || double.IsInfinity(degrees) || degrees % 90 != 0) return 0;
            int normalized = (int)(degrees % 360);
            return normalized < 0 ? normalized + 360 : normalized;
        }
    }

    private sealed class WalkedPages
    {
        private const int RootNode = 0;

        private readonly int[] _nodeParents;
        private readonly int[] _nodeKids;
        private readonly int[] _pageParents;
        private readonly int[] _pageKids;

        private WalkedPages(int[] nodeParents, int[] nodeKids, int[] pageParents, int[] pageKids)
        {
            _nodeParents = nodeParents;
            _nodeKids = nodeKids;
            _pageParents = pageParents;
            _pageKids = pageKids;
        }

        public int Count => _pageParents.Length;

        private sealed class Frame
        {
            public Frame(int node, PdfArray kids, int depth)
            {
                Node = node;
                Kids = kids;
                Depth = depth;
            }

            public int Node { get; }
            public PdfArray Kids { get; }
            public int Depth { get; }
            public int Next { get; set; }
        }

        public static WalkedPages Walk(PdfPageTree tree)
        {
            var store = tree._store;
            var nodeParents = new List<int> { -1 };
            var nodeKids = new List<int> { -1 };
            var pageParents = new List<int>();
            var pageKids = new List<int>();
            var visitedNodes = new HashSet<int>();
            if (tree._rootReference != null) visitedNodes.Add(tree._rootReference.ObjectNumber);

            var frames = new Stack<Frame>();
            if (store.Resolve(tree.Root().Get("Kids")) is PdfArray rootKids) frames.Push(new Frame(RootNode, rootKids, 0));
            while (frames.Count > 0)
            {
                var frame = frames.Peek();
                if (frame.Next >= frame.Kids.Items.Count)
                {
                    frames.Pop();
                    continue;
                }
                int kidIndex = frame.Next++;
                var item = frame.Kids.Items[kidIndex];
                if (tree.Summarize(item) is not { } kid) continue;
                if (tree.IsLeaf(kid))
                {
                    if (pageParents.Count >= MaxPages) throw new PdfFormatException("The document has too many pages.");
                    pageParents.Add(frame.Node);
                    pageKids.Add(kidIndex);
                    continue;
                }
                if (store.Resolve(item) is not PdfDictionary node || store.Resolve(node.Get("Kids")) is not PdfArray grandKids) continue;
                if (item is PdfReference reference && !visitedNodes.Add(reference.ObjectNumber))
                    throw new PdfFormatException("The page tree contains a cycle.");
                if (frame.Depth + 1 > MaxDepth) throw new PdfFormatException("The page tree is nested too deeply.");
                nodeParents.Add(frame.Node);
                nodeKids.Add(kidIndex);
                frames.Push(new Frame(nodeParents.Count - 1, grandKids, frame.Depth + 1));
            }
            return new WalkedPages(nodeParents.ToArray(), nodeKids.ToArray(), pageParents.ToArray(), pageKids.ToArray());
        }

        public PdfPageNode Find(PdfPageTree tree, int pageNumber)
        {
            var kidPath = new List<int> { _pageKids[pageNumber - 1] };
            for (int node = _pageParents[pageNumber - 1]; node != RootNode; node = _nodeParents[node])
                kidPath.Add(_nodeKids[node]);
            kidPath.Reverse();

            var store = tree._store;
            var dictionary = tree.Root();
            PdfReference? reference = tree._rootReference;
            var attributes = tree._rootAttributes;
            foreach (int kidIndex in kidPath)
            {
                if (store.Resolve(dictionary.Get("Kids")) is not PdfArray kids || kidIndex >= kids.Items.Count ||
                    store.Resolve(kids.Items[kidIndex]) is not PdfDictionary kid)
                    throw new PdfFormatException("A page tree node changed while it was being read.");
                reference = kids.Items[kidIndex] as PdfReference;
                dictionary = kid;
                attributes = attributes.Inherit(store, kid);
            }
            return PageNode(pageNumber, dictionary, reference, attributes);
        }
    }
}
