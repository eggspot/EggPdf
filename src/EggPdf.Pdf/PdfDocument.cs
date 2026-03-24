using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Generates a valid PDF 1.7 document. Handles object numbering, xref table, and trailer.
/// </summary>
public class PdfDocument
{
    private readonly List<PdfPage> _pages = new();
    private readonly Dictionary<string, PdfImage> _images = new();
    private readonly Dictionary<string, EmbeddedFontData> _embeddedFonts = new();
    private List<PdfBookmark>? _bookmarks;
    public string? Title { get; set; }
    public string? Author { get; set; }

    /// <summary>Register an embedded TrueType font for CIDFont Type 2 embedding.</summary>
    public void AddEmbeddedFont(string fontName, byte[] subsetData, Dictionary<int, ushort> codepointToGid, ushort[] widths,
        int unitsPerEm, int ascent, int descent)
    {
        _embeddedFonts[fontName] = new EmbeddedFontData
        {
            SubsetData = subsetData,
            CodepointToGlyphId = codepointToGid,
            Widths = widths,
            UnitsPerEm = unitsPerEm,
            Ascent = ascent,
            Descent = descent,
        };
    }

    /// <summary>Check if a font is embedded (CIDFont) vs built-in Type1.</summary>
    public bool IsEmbeddedFont(string fontName) => _embeddedFonts.ContainsKey(fontName);

    /// <summary>Convert text to glyph IDs for an embedded CIDFont.</summary>
    public ushort[]? GetGlyphIds(string fontName, string text)
    {
        if (!_embeddedFonts.TryGetValue(fontName, out var fontData))
            return null;

        var glyphIds = new ushort[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            int codepoint = text[i];
            // Handle surrogate pairs
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codepoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++; // skip low surrogate
            }

            if (fontData.CodepointToGlyphId.TryGetValue(codepoint, out var gid))
                glyphIds[i] = gid;
            // else stays 0 (.notdef)
        }
        return glyphIds;
    }

    /// <summary>Set bookmarks (document outline) to include in the PDF.</summary>
    public void SetBookmarks(List<PdfBookmark> bookmarks)
    {
        _bookmarks = bookmarks != null && bookmarks.Count > 0 ? bookmarks : null;
    }

    /// <summary>Add a page with dimensions in PDF points.</summary>
    public PdfPage AddPage(float widthPt, float heightPt)
    {
        var page = new PdfPage(widthPt, heightPt);
        _pages.Add(page);
        return page;
    }

    /// <summary>Register an image for embedding. Returns the image name for reference.</summary>
    public string AddImage(PdfImage image)
    {
        _images[image.Name] = image;
        return image.Name;
    }

    /// <summary>Write the PDF to a byte array.</summary>
    public byte[] ToByteArray()
    {
        using var ms = new MemoryStream();
        WriteTo(ms);
        return ms.ToArray();
    }

    /// <summary>Write the PDF to a stream.</summary>
    public void WriteTo(Stream output)
    {
        var writer = new PdfStreamWriter(output);

        // Header
        writer.WriteLine("%PDF-1.7");
        writer.WriteLine("%\xE2\xE3\xCF\xD3"); // binary marker

        // Collect all fonts used across pages
        var allFonts = new HashSet<string>();
        foreach (var page in _pages)
            foreach (var font in page.UsedFonts)
                allFonts.Add(font);

        // Object numbering plan:
        // 1: Catalog
        // 2: Pages
        // 3..N: Page objects (each page = page dict + content stream = 2 objects)
        // Then: font resources, info dict
        int nextObj = 1;
        int catalogObj = nextObj++;
        int pagesObj = nextObj++;

        // Pre-calculate page objects
        var pageObjs = new List<(int pageDict, int contentStream, int? annotArray)>();
        foreach (var page in _pages)
        {
            int pd = nextObj++;
            int cs = nextObj++;
            int? ann = page.Links.Count > 0 ? nextObj++ : null;
            if (page.Links.Count > 0)
            {
                foreach (var _ in page.Links)
                    nextObj++; // annotation objects
            }
            pageObjs.Add((pd, cs, ann));
        }

        // Font objects — Type1 (built-in) get 1 object, CIDFont (embedded) get 5 objects
        var fontObjs = new Dictionary<string, int>(); // main font object ref
        var cidFontObjs = new Dictionary<string, (int type0, int cidFont, int descriptor, int stream, int toUnicode)>();
        foreach (var font in allFonts)
        {
            if (_embeddedFonts.ContainsKey(font))
            {
                int type0 = nextObj++;
                int cidFont = nextObj++;
                int descriptor = nextObj++;
                int stream = nextObj++;
                int toUnicode = nextObj++;
                fontObjs[font] = type0;
                cidFontObjs[font] = (type0, cidFont, descriptor, stream, toUnicode);
            }
            else
            {
                fontObjs[font] = nextObj++;
            }
        }

        // ExtGState objects for opacity
        var allExtGStates = new HashSet<string>();
        foreach (var page in _pages)
            foreach (var gs in page.UsedExtGStates)
                allExtGStates.Add(gs);
        var extGStateObjs = new Dictionary<string, int>();
        foreach (var gs in allExtGStates)
            extGStateObjs[gs] = nextObj++;

        // Image XObject objects (each image = 1 object, + 1 SMask if alpha)
        var imageObjs = new Dictionary<string, int>();
        var imageSMaskObjs = new Dictionary<string, int>();
        foreach (var kv in _images)
        {
            imageObjs[kv.Key] = nextObj++;
            if (kv.Value.SMaskData != null)
                imageSMaskObjs[kv.Key] = nextObj++;
        }

        // Info dictionary
        int infoObj = nextObj++;

        // Outline objects for bookmarks
        int outlineRootObj = 0;
        var outlineItemObjs = new List<int>();
        if (_bookmarks != null && _bookmarks.Count > 0)
        {
            outlineRootObj = nextObj++;
            for (int i = 0; i < _bookmarks.Count; i++)
                outlineItemObjs.Add(nextObj++);
        }

        var offsets = new Dictionary<int, long>();

        // Write Catalog
        offsets[catalogObj] = writer.Position;
        writer.WriteLine($"{catalogObj} 0 obj");
        if (outlineRootObj > 0)
            writer.WriteLine($"<< /Type /Catalog /Pages {pagesObj} 0 R /Outlines {outlineRootObj} 0 R >>");
        else
            writer.WriteLine($"<< /Type /Catalog /Pages {pagesObj} 0 R >>");
        writer.WriteLine("endobj");

        // Write Pages
        offsets[pagesObj] = writer.Position;
        writer.WriteLine($"{pagesObj} 0 obj");
        var kids = string.Join(" ", pageObjs.ConvertAll(p => $"{p.pageDict} 0 R"));
        writer.WriteLine($"<< /Type /Pages /Kids [{kids}] /Count {_pages.Count} >>");
        writer.WriteLine("endobj");

        // Write font objects
        foreach (var kv in fontObjs)
        {
            if (cidFontObjs.TryGetValue(kv.Key, out var cid))
            {
                // Write CIDFont Type 2 (embedded TrueType)
                WriteCIDFont(writer, offsets, kv.Key, cid, _embeddedFonts[kv.Key]);
            }
            else
            {
                // Write built-in Type1 font
                offsets[kv.Value] = writer.Position;
                writer.WriteLine($"{kv.Value} 0 obj");
                writer.WriteLine($"<< /Type /Font /Subtype /Type1 /BaseFont /{kv.Key} >>");
                writer.WriteLine("endobj");
            }
        }

        // Write ExtGState objects for opacity
        foreach (var kv in extGStateObjs)
        {
            // Extract opacity from name: "GS50" -> 0.50
            float opacity = 1.0f;
            if (kv.Key.StartsWith("GS") && int.TryParse(kv.Key.Substring(2), out int pct))
                opacity = pct / 100f;

            offsets[kv.Value] = writer.Position;
            writer.WriteLine($"{kv.Value} 0 obj");
            writer.WriteLine($"<< /Type /ExtGState /ca {F(opacity)} /CA {F(opacity)} >>");
            writer.WriteLine("endobj");
        }

        // Write image SMask objects first (they're referenced by image objects)
        foreach (var kv in imageSMaskObjs)
        {
            var img = _images[kv.Key];
            var smaskData = img.SMaskData!;

            // Compress with Deflate
            byte[] compressedSmask;
            using (var sms = new MemoryStream())
            {
                using (var ds = new DeflateStream(sms, CompressionLevel.Fastest, true))
                    ds.Write(smaskData, 0, smaskData.Length);
                compressedSmask = sms.ToArray();
            }

            offsets[kv.Value] = writer.Position;
            writer.WriteLine($"{kv.Value} 0 obj");
            writer.WriteLine($"<< /Type /XObject /Subtype /Image /Width {img.Width} /Height {img.Height}");
            writer.WriteLine($"/ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode /Length {compressedSmask.Length} >>");
            writer.WriteLine("stream");
            writer.WriteBytes(compressedSmask);
            writer.WriteLine("");
            writer.WriteLine("endstream");
            writer.WriteLine("endobj");
        }

        // Write image XObjects
        foreach (var kv in imageObjs)
        {
            var img = _images[kv.Key];

            offsets[kv.Value] = writer.Position;
            writer.WriteLine($"{kv.Value} 0 obj");

            if (img.Format == PdfImageFormat.Jpeg)
            {
                // JPEG: pass-through with DCTDecode
                var imgDict = new StringBuilder();
                imgDict.Append($"<< /Type /XObject /Subtype /Image /Width {img.Width} /Height {img.Height}");
                imgDict.Append($" /ColorSpace /DeviceRGB /BitsPerComponent {img.BitsPerComponent}");
                imgDict.Append($" /Filter /DCTDecode /Length {img.Data.Length}");
                imgDict.Append(" >>");
                writer.WriteLine(imgDict.ToString());
                writer.WriteLine("stream");
                writer.WriteBytes(img.Data);
                writer.WriteLine("");
                writer.WriteLine("endstream");
            }
            else
            {
                // Raw RGB: compress with Deflate
                byte[] compressed;
                using (var ims = new MemoryStream())
                {
                    using (var ds = new DeflateStream(ims, CompressionLevel.Fastest, true))
                        ds.Write(img.Data, 0, img.Data.Length);
                    compressed = ims.ToArray();
                }

                var imgDict = new StringBuilder();
                imgDict.Append($"<< /Type /XObject /Subtype /Image /Width {img.Width} /Height {img.Height}");
                imgDict.Append($" /ColorSpace /DeviceRGB /BitsPerComponent {img.BitsPerComponent}");
                imgDict.Append($" /Filter /FlateDecode /Length {compressed.Length}");
                if (imageSMaskObjs.TryGetValue(kv.Key, out int smaskObj))
                    imgDict.Append($" /SMask {smaskObj} 0 R");
                imgDict.Append(" >>");
                writer.WriteLine(imgDict.ToString());
                writer.WriteLine("stream");
                writer.WriteBytes(compressed);
                writer.WriteLine("");
                writer.WriteLine("endstream");
            }
            writer.WriteLine("endobj");
        }

        // Build font resource dict reference
        var fontResources = new StringBuilder();
        fontResources.Append("<< ");
        foreach (var kv in fontObjs)
            fontResources.Append($"/{kv.Key} {kv.Value} 0 R ");
        fontResources.Append(">>");

        // Build XObject resource dict reference
        var xobjectResources = new StringBuilder();
        if (imageObjs.Count > 0)
        {
            xobjectResources.Append("<< ");
            foreach (var kv in imageObjs)
                xobjectResources.Append($"/{kv.Key} {kv.Value} 0 R ");
            xobjectResources.Append(">>");
        }

        // Write each page
        for (int i = 0; i < _pages.Count; i++)
        {
            var page = _pages[i];
            var (pageDictObj, contentStreamObj, annotArrayObj) = pageObjs[i];

            // Content stream
            var contentBytes = Encoding.ASCII.GetBytes(page.ContentStream.ToString());

            offsets[contentStreamObj] = writer.Position;
            writer.WriteLine($"{contentStreamObj} 0 obj");
            writer.WriteLine($"<< /Length {contentBytes.Length} >>");
            writer.WriteLine("stream");
            writer.WriteBytes(contentBytes);
            writer.WriteLine("");
            writer.WriteLine("endstream");
            writer.WriteLine("endobj");

            // Link annotations
            var annotRefs = new List<string>();
            if (page.Links.Count > 0)
            {
                int annotStartObj = annotArrayObj!.Value + 1 - page.Links.Count; // wrong calc, let me fix
                // Actually we need to track annotation object numbers properly
            }

            // Write annotation objects
            var annotObjNumbers = new List<int>();
            if (page.Links.Count > 0)
            {
                foreach (var link in page.Links)
                {
                    // Find next annotation obj number
                    // We already allocated them sequentially after annotArrayObj
                }
            }

            // Page dictionary
            offsets[pageDictObj] = writer.Position;
            writer.WriteLine($"{pageDictObj} 0 obj");
            var pageDict = new StringBuilder();
            pageDict.Append("<< /Type /Page");
            pageDict.Append($" /Parent {pagesObj} 0 R");
            pageDict.Append($" /MediaBox [0 0 {F(page.WidthPt)} {F(page.HeightPt)}]");
            pageDict.Append($" /Contents {contentStreamObj} 0 R");
            // Resources
            bool hasResources = allFonts.Count > 0 || imageObjs.Count > 0 || extGStateObjs.Count > 0;
            if (hasResources)
            {
                pageDict.Append(" /Resources <<");
                if (allFonts.Count > 0)
                    pageDict.Append($" /Font {fontResources}");
                if (imageObjs.Count > 0)
                    pageDict.Append($" /XObject {xobjectResources}");
                if (extGStateObjs.Count > 0)
                {
                    pageDict.Append(" /ExtGState <<");
                    foreach (var gs in extGStateObjs)
                        pageDict.Append($" /{gs.Key} {gs.Value} 0 R");
                    pageDict.Append(" >>");
                }
                pageDict.Append(" >>");
            }

            // Add link annotations inline (simpler approach)
            if (page.Links.Count > 0)
            {
                pageDict.Append(" /Annots [");
                foreach (var link in page.Links)
                {
                    float x1 = link.X;
                    float y1 = link.Y;
                    float x2 = link.X + link.Width;
                    float y2 = link.Y + link.Height;
                    pageDict.Append($" << /Type /Annot /Subtype /Link /Rect [{F(x1)} {F(y1)} {F(x2)} {F(y2)}]");
                    pageDict.Append($" /Border [0 0 0]");
                    pageDict.Append($" /A << /Type /Action /S /URI /URI ({link.Url}) >> >>");
                }
                pageDict.Append(" ]");
            }

            pageDict.Append(" >>");
            writer.WriteLine(pageDict.ToString());
            writer.WriteLine("endobj");
        }

        // Write outline objects (bookmarks)
        if (_bookmarks != null && _bookmarks.Count > 0 && outlineRootObj > 0)
        {
            WriteOutlineObjects(writer, offsets, pageObjs, outlineRootObj, outlineItemObjs);
        }

        // Info dictionary
        offsets[infoObj] = writer.Position;
        writer.WriteLine($"{infoObj} 0 obj");
        var info = new StringBuilder();
        info.Append("<< /Producer (EggPdf)");
        if (!string.IsNullOrEmpty(Title))
            info.Append($" /Title ({EscapePdfString(Title)})");
        if (!string.IsNullOrEmpty(Author))
            info.Append($" /Author ({EscapePdfString(Author)})");
        info.Append($" /CreationDate (D:{DateTime.UtcNow:yyyyMMddHHmmss}Z)");
        info.Append(" >>");
        writer.WriteLine(info.ToString());
        writer.WriteLine("endobj");

        // Cross-reference table
        long xrefOffset = writer.Position;
        int totalObjects = nextObj;
        writer.WriteLine("xref");
        writer.WriteLine($"0 {totalObjects}");
        writer.WriteLine("0000000000 65535 f ");

        for (int obj = 1; obj < totalObjects; obj++)
        {
            if (offsets.TryGetValue(obj, out long offset))
                writer.WriteLine($"{offset:D10} 00000 n ");
            else
                writer.WriteLine("0000000000 00000 f ");
        }

        // Trailer
        writer.WriteLine("trailer");
        writer.WriteLine($"<< /Size {totalObjects} /Root {catalogObj} 0 R /Info {infoObj} 0 R >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefOffset.ToString());
        writer.WriteLine("%%EOF");
    }

    /// <summary>
    /// Writes the PDF outline (bookmark) tree objects.
    /// Builds a nested hierarchy from the flat list of headings based on their levels.
    /// </summary>
    private void WriteOutlineObjects(
        PdfStreamWriter writer,
        Dictionary<int, long> offsets,
        List<(int pageDict, int contentStream, int? annotArray)> pageObjs,
        int outlineRootObj,
        List<int> outlineItemObjs)
    {
        // Build tree structure from flat list:
        // Each node tracks: parentObjId, firstChildIdx, lastChildIdx, prevIdx, nextIdx, childCount
        int count = _bookmarks!.Count;
        var parentObj = new int[count];     // PDF object number of the parent
        var firstChild = new int[count];    // index of first child (-1 = none)
        var lastChild = new int[count];     // index of last child (-1 = none)
        var prevSibling = new int[count];   // index of prev sibling (-1 = none)
        var nextSibling = new int[count];   // index of next sibling (-1 = none)
        var childCount = new int[count];    // total visible descendant count

        for (int i = 0; i < count; i++)
        {
            parentObj[i] = outlineRootObj;  // default: root is parent
            firstChild[i] = -1;
            lastChild[i] = -1;
            prevSibling[i] = -1;
            nextSibling[i] = -1;
        }

        // Build parent-child relationships based on heading levels.
        // Use a stack to track the "current path" of ancestor indices.
        // For each bookmark, find its parent by walking back through ancestors
        // to find the last bookmark with a smaller level.
        var ancestorStack = new List<int>(); // indices of ancestor bookmarks

        // Track top-level children (direct children of the outline root)
        var topLevelChildren = new List<int>();

        for (int i = 0; i < count; i++)
        {
            int level = _bookmarks[i].Level;

            // Pop ancestors that are not parents of this item
            // A parent must have a strictly smaller level
            while (ancestorStack.Count > 0)
            {
                int ancestorIdx = ancestorStack[ancestorStack.Count - 1];
                if (_bookmarks[ancestorIdx].Level < level)
                    break;
                ancestorStack.RemoveAt(ancestorStack.Count - 1);
            }

            if (ancestorStack.Count > 0)
            {
                // This item is a child of the last ancestor
                int parentIdx = ancestorStack[ancestorStack.Count - 1];
                parentObj[i] = outlineItemObjs[parentIdx];

                if (firstChild[parentIdx] == -1)
                {
                    firstChild[parentIdx] = i;
                }
                else
                {
                    // Link as next sibling of the current last child
                    int prevIdx = lastChild[parentIdx];
                    nextSibling[prevIdx] = i;
                    prevSibling[i] = prevIdx;
                }
                lastChild[parentIdx] = i;
            }
            else
            {
                // Top-level child (parent is outline root)
                parentObj[i] = outlineRootObj;
                if (topLevelChildren.Count > 0)
                {
                    int prevIdx = topLevelChildren[topLevelChildren.Count - 1];
                    nextSibling[prevIdx] = i;
                    prevSibling[i] = prevIdx;
                }
                topLevelChildren.Add(i);
            }

            ancestorStack.Add(i);
        }

        // Calculate child counts (total visible descendants) bottom-up
        // We do a simple recursive count
        for (int i = 0; i < count; i++)
            childCount[i] = CountDescendants(i, firstChild, nextSibling);

        // Write outline root
        int rootFirst = topLevelChildren.Count > 0 ? topLevelChildren[0] : 0;
        int rootLast = topLevelChildren.Count > 0 ? topLevelChildren[topLevelChildren.Count - 1] : 0;
        int totalTopLevel = topLevelChildren.Count;

        offsets[outlineRootObj] = writer.Position;
        writer.WriteLine($"{outlineRootObj} 0 obj");
        writer.WriteLine($"<< /Type /Outlines /First {outlineItemObjs[rootFirst]} 0 R /Last {outlineItemObjs[rootLast]} 0 R /Count {totalTopLevel} >>");
        writer.WriteLine("endobj");

        // Write each outline item
        for (int i = 0; i < count; i++)
        {
            var bm = _bookmarks[i];
            int objNum = outlineItemObjs[i];

            // Resolve destination: page reference + Y position
            int pageIdx = Math.Min(bm.PageIndex, pageObjs.Count - 1);
            if (pageIdx < 0) pageIdx = 0;
            int pageRef = pageObjs[pageIdx].pageDict;

            var entry = new StringBuilder();
            entry.Append("<< /Title (");
            entry.Append(EscapePdfString(bm.Title));
            entry.Append(")");
            entry.Append($" /Parent {parentObj[i]} 0 R");
            entry.Append($" /Dest [{pageRef} 0 R /XYZ 0 {F(bm.TopPt)} 0]");

            if (firstChild[i] >= 0)
            {
                entry.Append($" /First {outlineItemObjs[firstChild[i]]} 0 R");
                entry.Append($" /Last {outlineItemObjs[lastChild[i]]} 0 R");
                entry.Append($" /Count {childCount[i]}");
            }

            if (prevSibling[i] >= 0)
                entry.Append($" /Prev {outlineItemObjs[prevSibling[i]]} 0 R");
            if (nextSibling[i] >= 0)
                entry.Append($" /Next {outlineItemObjs[nextSibling[i]]} 0 R");

            entry.Append(" >>");

            offsets[objNum] = writer.Position;
            writer.WriteLine($"{objNum} 0 obj");
            writer.WriteLine(entry.ToString());
            writer.WriteLine("endobj");
        }
    }

    /// <summary>Count total descendants of an outline node.</summary>
    private static int CountDescendants(int index, int[] firstChild, int[] nextSibling)
    {
        int total = 0;
        int child = firstChild[index];
        while (child >= 0)
        {
            total++; // count this child
            total += CountDescendants(child, firstChild, nextSibling); // count its descendants
            child = nextSibling[child];
        }
        return total;
    }

    /// <summary>Write a CIDFont Type 2 (embedded TrueType) with all required objects.</summary>
    private void WriteCIDFont(PdfStreamWriter writer, Dictionary<int, long> offsets,
        string fontName, (int type0, int cidFont, int descriptor, int stream, int toUnicode) objs,
        EmbeddedFontData fontData)
    {
        // 1. Compress the subset font data
        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var ds = new DeflateStream(ms, CompressionLevel.Fastest, true))
                ds.Write(fontData.SubsetData, 0, fontData.SubsetData.Length);
            compressed = ms.ToArray();
        }

        // 2. Build W (widths) array: /W [0 [w0 w1 w2 ...]]
        var wArray = new StringBuilder();
        wArray.Append("[0 [");
        for (int i = 0; i < fontData.Widths.Length; i++)
        {
            if (i > 0) wArray.Append(' ');
            // Convert from font units to 1/1000 of text space
            int w = fontData.UnitsPerEm > 0 ? (int)(fontData.Widths[i] * 1000L / fontData.UnitsPerEm) : fontData.Widths[i];
            wArray.Append(w);
        }
        wArray.Append("]]");

        // 3. Build ToUnicode CMap
        byte[] toUnicodeData = BuildToUnicodeCMap(fontData.CodepointToGlyphId);

        byte[] toUnicodeCompressed;
        using (var ms = new MemoryStream())
        {
            using (var ds = new DeflateStream(ms, CompressionLevel.Fastest, true))
                ds.Write(toUnicodeData, 0, toUnicodeData.Length);
            toUnicodeCompressed = ms.ToArray();
        }

        // 4. Write font stream (subset TrueType data)
        offsets[objs.stream] = writer.Position;
        writer.WriteLine($"{objs.stream} 0 obj");
        writer.WriteLine($"<< /Length {compressed.Length} /Length1 {fontData.SubsetData.Length} /Filter /FlateDecode >>");
        writer.WriteLine("stream");
        writer.WriteBytes(compressed);
        writer.WriteLine("");
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");

        // 5. Write font descriptor
        int ascent = fontData.UnitsPerEm > 0 ? fontData.Ascent * 1000 / fontData.UnitsPerEm : fontData.Ascent;
        int descent = fontData.UnitsPerEm > 0 ? fontData.Descent * 1000 / fontData.UnitsPerEm : fontData.Descent;
        int flags = 32; // Nonsymbolic

        offsets[objs.descriptor] = writer.Position;
        writer.WriteLine($"{objs.descriptor} 0 obj");
        writer.WriteLine($"<< /Type /FontDescriptor /FontName /{fontName}");
        writer.WriteLine($"/Flags {flags} /Ascent {ascent} /Descent {descent}");
        writer.WriteLine($"/ItalicAngle 0 /CapHeight {ascent} /StemV 80");
        writer.WriteLine($"/FontBBox [0 {descent} 1000 {ascent}]");
        writer.WriteLine($"/FontFile2 {objs.stream} 0 R >>");
        writer.WriteLine("endobj");

        // 6. Write CIDFont dictionary
        offsets[objs.cidFont] = writer.Position;
        writer.WriteLine($"{objs.cidFont} 0 obj");
        writer.WriteLine($"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{fontName}");
        writer.WriteLine($"/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >>");
        writer.WriteLine($"/FontDescriptor {objs.descriptor} 0 R");
        writer.WriteLine($"/W {wArray}");
        writer.WriteLine($"/DW 1000 >>");
        writer.WriteLine("endobj");

        // 7. Write ToUnicode CMap
        offsets[objs.toUnicode] = writer.Position;
        writer.WriteLine($"{objs.toUnicode} 0 obj");
        writer.WriteLine($"<< /Length {toUnicodeCompressed.Length} /Filter /FlateDecode >>");
        writer.WriteLine("stream");
        writer.WriteBytes(toUnicodeCompressed);
        writer.WriteLine("");
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");

        // 8. Write Type0 font (the top-level font reference)
        offsets[objs.type0] = writer.Position;
        writer.WriteLine($"{objs.type0} 0 obj");
        writer.WriteLine($"<< /Type /Font /Subtype /Type0 /BaseFont /{fontName}");
        writer.WriteLine($"/Encoding /Identity-H");
        writer.WriteLine($"/DescendantFonts [{objs.cidFont} 0 R]");
        writer.WriteLine($"/ToUnicode {objs.toUnicode} 0 R >>");
        writer.WriteLine("endobj");
    }

    /// <summary>Build a ToUnicode CMap for text extraction from CIDFont.</summary>
    private static byte[] BuildToUnicodeCMap(Dictionary<int, ushort> codepointToGid)
    {
        var sb = new StringBuilder();
        sb.AppendLine("/CIDInit /ProcSet findresource begin");
        sb.AppendLine("12 dict begin");
        sb.AppendLine("begincmap");
        sb.AppendLine("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def");
        sb.AppendLine("/CMapName /Adobe-Identity-UCS def");
        sb.AppendLine("/CMapType 2 def");
        sb.AppendLine("1 begincodespacerange");
        sb.AppendLine("<0000> <FFFF>");
        sb.AppendLine("endcodespacerange");

        // Build reverse mapping: glyph ID -> Unicode codepoint
        var gidToCodepoint = new Dictionary<ushort, int>();
        foreach (var kv in codepointToGid)
        {
            if (!gidToCodepoint.ContainsKey(kv.Value))
                gidToCodepoint[kv.Value] = kv.Key;
        }

        // Write in batches of 100 (PDF limit per beginbfchar block)
        var entries = gidToCodepoint.OrderBy(kv => kv.Key).ToList();
        int idx = 0;
        while (idx < entries.Count)
        {
            int batchSize = Math.Min(100, entries.Count - idx);
            sb.AppendLine($"{batchSize} beginbfchar");
            for (int i = 0; i < batchSize; i++)
            {
                var entry = entries[idx + i];
                if (entry.Value <= 0xFFFF)
                    sb.AppendLine($"<{entry.Key:X4}> <{entry.Value:X4}>");
                else
                {
                    // Supplementary plane: encode as UTF-16 surrogate pair
                    int hi = 0xD800 + ((entry.Value - 0x10000) >> 10);
                    int lo = 0xDC00 + ((entry.Value - 0x10000) & 0x3FF);
                    sb.AppendLine($"<{entry.Key:X4}> <{hi:X4}{lo:X4}>");
                }
            }
            sb.AppendLine("endbfchar");
            idx += batchSize;
        }

        sb.AppendLine("endcmap");
        sb.AppendLine("CMapName currentdict /CMap defineresource pop");
        sb.AppendLine("end");
        sb.AppendLine("end");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static string F(float value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static string EscapePdfString(string text)
        => text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}

/// <summary>Data for an embedded TrueType font (CIDFont Type 2).</summary>
internal class EmbeddedFontData
{
    public byte[] SubsetData { get; set; } = Array.Empty<byte>();
    public Dictionary<int, ushort> CodepointToGlyphId { get; set; } = new();
    public ushort[] Widths { get; set; } = Array.Empty<ushort>();
    public int UnitsPerEm { get; set; }
    public int Ascent { get; set; }
    public int Descent { get; set; }
}

/// <summary>Helper for tracking byte positions while writing.</summary>
internal class PdfStreamWriter
{
    private readonly Stream _stream;
    public long Position => _stream.Position;

    public PdfStreamWriter(Stream stream) { _stream = stream; }

    public void WriteLine(string line)
    {
        var bytes = Encoding.ASCII.GetBytes(line + "\n");
        _stream.Write(bytes, 0, bytes.Length);
    }

    public void WriteBytes(byte[] data)
    {
        _stream.Write(data, 0, data.Length);
    }
}
