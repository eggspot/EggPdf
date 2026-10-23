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
public partial class PdfDocument
{
    private static readonly Encoding Latin1Encoding = Encoding.GetEncoding(28591);

    private readonly List<PdfPage> _pages = new();
    private readonly Dictionary<string, PdfImage> _images = new();
    private readonly Dictionary<string, EmbeddedFontData> _embeddedFonts = new();
    private List<PdfBookmark>? _bookmarks;
    public string? Title { get; set; }
    public string? Author { get; set; }

    /// <summary>Optional encryption settings. When set, the PDF will be encrypted.</summary>
    public PdfEncryption? Encryption { get; set; }

    internal static bool DefaultCompressContentStreams = true;

    /// <summary>
    /// Compress page content streams with FlateDecode (zlib). Enabled by
    /// default; typically shrinks text-heavy pages by 50-70%.
    /// </summary>
    public bool CompressContentStreams { get; set; } = DefaultCompressContentStreams;

    /// <summary>Register an embedded TrueType font for CIDFont Type 2 embedding.</summary>
    public void AddEmbeddedFont(string fontName, byte[] subsetData, Dictionary<int, ushort> codepointToGid, ushort[] widths,
        int unitsPerEm, int ascent, int descent,
        Dictionary<int, List<(ushort newGlyphId, float r, float g, float b, bool useTextColor)>>? colorLayers = null)
    {
        _embeddedFonts[fontName] = new EmbeddedFontData
        {
            SubsetData = subsetData,
            CodepointToGlyphId = codepointToGid,
            Widths = widths,
            UnitsPerEm = unitsPerEm,
            Ascent = ascent,
            Descent = descent,
            ColorLayers = colorLayers,
        };
    }

    /// <summary>Check if a font is embedded (CIDFont) vs built-in Type1.</summary>
    public bool IsEmbeddedFont(string fontName) => _embeddedFonts.ContainsKey(fontName);

    /// <summary>Convert text to glyph IDs for an embedded CIDFont.</summary>
    /// <summary>Look up a single codepoint's glyph ID in an embedded font.</summary>
    public bool TryGetGlyphId(string fontName, int codepoint, out ushort gid)
    {
        gid = 0;
        return _embeddedFonts.TryGetValue(fontName, out var fontData) &&
               fontData.CodepointToGlyphId.TryGetValue(codepoint, out gid);
    }

    /// <summary>True if any codepoint in text has COLR color-glyph layers in this embedded font.</summary>
    public bool ContainsColorGlyphs(string fontName, string text)
    {
        if (!_embeddedFonts.TryGetValue(fontName, out var fontData) || fontData.ColorLayers == null || fontData.ColorLayers.Count == 0)
            return false;

        for (int i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            if (fontData.ColorLayers.ContainsKey(cp)) return true;
        }
        return false;
    }

    /// <summary>Look up a codepoint's COLR color-glyph layers (already resolved to colors and new glyph IDs).</summary>
    public bool TryGetColorLayers(string fontName, int codepoint,
        out List<(ushort newGlyphId, float r, float g, float b, bool useTextColor)>? layers)
    {
        layers = null;
        return _embeddedFonts.TryGetValue(fontName, out var fontData) &&
               fontData.ColorLayers != null &&
               fontData.ColorLayers.TryGetValue(codepoint, out layers);
    }

    /// <summary>Glyph advance width in PDF user-space points, for manual per-glyph positioning (color-glyph runs).</summary>
    public bool TryGetGlyphAdvancePt(string fontName, ushort glyphId, float fontSizePt, out float advancePt)
    {
        advancePt = 0;
        if (!_embeddedFonts.TryGetValue(fontName, out var fontData) || fontData.UnitsPerEm <= 0)
            return false;

        ushort w;
        if (glyphId < fontData.Widths.Length)
            w = fontData.Widths[glyphId];
        else
            w = fontData.Widths.Length > 0 ? fontData.Widths[fontData.Widths.Length - 1] : (ushort)0;

        advancePt = w / (float)fontData.UnitsPerEm * fontSizePt;
        return true;
    }

    public ushort[]? GetGlyphIds(string fontName, string text)
    {
        if (!_embeddedFonts.TryGetValue(fontName, out var fontData))
            return null;

        var glyphIds = new ushort[text.Length];
        int writeCount = 0;
        for (int i = 0; i < text.Length; i++)
        {
            int codepoint = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codepoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            if (fontData.CodepointToGlyphId.TryGetValue(codepoint, out var gid))
                glyphIds[writeCount] = gid;
            writeCount++;
        }

        if (writeCount == text.Length) return glyphIds;
        var result = new ushort[writeCount];
        Array.Copy(glyphIds, result, writeCount);
        return result;
    }

    /// <summary>Number of pages added so far.</summary>
    public int PageCount => _pages.Count;

    /// <summary>Append bookmarks to the outline (used when a document is rendered in several page groups).</summary>
    public void AppendBookmarks(List<PdfBookmark> bookmarks)
    {
        if (bookmarks == null || bookmarks.Count == 0) return;
        if (_bookmarks == null) _bookmarks = new List<PdfBookmark>(bookmarks);
        else _bookmarks.AddRange(bookmarks);
    }

    /// <summary>Set bookmarks (document outline) to include in the PDF.</summary>
    public void SetBookmarks(List<PdfBookmark> bookmarks)
    {
        _bookmarks = bookmarks != null && bookmarks.Count > 0 ? bookmarks : null;
    }

    /// <summary>Add a page with dimensions in PDF points.</summary>
    public PdfPage AddPage(float widthPt, float heightPt)
    {
        var page = new PdfPage(widthPt, heightPt) { PageIndex = _pages.Count };
        _pages.Add(page);
        return page;
    }

    /// <summary>Register an image for embedding. Returns the image name for reference.</summary>
    public string AddImage(PdfImage image)
    {
        _images[image.Name] = image;
        return image.Name;
    }

    /// <summary>The image previously registered under <paramref name="name"/>, or null.</summary>
    public PdfImage? GetImage(string name) => _images.TryGetValue(name, out var image) ? image : null;

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
        if (Conformance != null && Encryption != null)
            throw new InvalidOperationException("PDF/A conformance forbids encryption (ISO 19005 disallows /Encrypt).");
        if (Invoice != null && Conformance != PdfAConformance.PdfA3b && Conformance != PdfAConformance.PdfA3u)
            throw new InvalidOperationException("Factur-X/ZUGFeRD invoice attachment requires PDF/A-3 conformance (PdfA3b or PdfA3u).");
        if (StructureTree != null && Encryption != null)
            throw new InvalidOperationException("Tagged PDF (PDF/UA-1) output does not support encryption.");
        if (Conformance != null && Conformance.Value.RequiresTagging() && StructureTree == null)
            throw new InvalidOperationException($"{Conformance} conformance requires accessibility tagging -- set StructureTree (e.g. via HtmlToPdf.Render(html, conformance, tagged: true)) or use the corresponding b/u level instead.");
        if (StructureTree != null && UaVersion == PdfUaVersion.Ua2 && Conformance != null)
            throw new InvalidOperationException("PDF/UA-2 combined with PDF/A conformance is not a defined joint standard -- use PDF/UA-1 (tagged: true with a PdfAConformance b/u/a level) for combined archival+accessibility conformance instead.");
        ValidatePdfA1NoTransparency();

        var writer = new PdfStreamWriter(output);

        foreach (var page in _pages)
            page.Links.RemoveAll(l => l.TargetId != null && !_anchors.ContainsKey(l.TargetId) && !IsDocumentTop(l.TargetId));

        EncryptionParams? enc = null;
        byte[] encDocId = Array.Empty<byte>();
        if (Encryption != null)
        {
            encDocId = new byte[16];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                rng.GetBytes(encDocId);
            enc = Encryption.Compute(encDocId);
        }
        else if (Conformance != null)
        {
            encDocId = new byte[16];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                rng.GetBytes(encDocId);
        }

        writer.WriteLine(PdfVersionHeader);
        writer.WriteLine("%\xE2\xE3\xCF\xD3");

        var allFonts = new HashSet<string>();
        foreach (var page in _pages)
            foreach (var font in page.UsedFonts)
                allFonts.Add(font);

        var alloc = new PdfObjectAllocator();
        int catalogObj = alloc.Allocate();
        int pagesObj = alloc.Allocate();

        var pageObjs = new List<(int pageDict, int contentStream)>();
        foreach (var page in _pages)
        {
            int pd = alloc.Allocate();
            int cs = alloc.Allocate();
            pageObjs.Add((pd, cs));
        }

        var fontObjs = new Dictionary<string, int>();
        var cidFontObjs = new Dictionary<string, (int type0, int cidFont, int descriptor, int stream, int toUnicode)>();
        foreach (var font in allFonts)
        {
            if (_embeddedFonts.ContainsKey(font))
            {
                int type0 = alloc.Allocate();
                int cidFont = alloc.Allocate();
                int descriptor = alloc.Allocate();
                int stream = alloc.Allocate();
                int toUnicode = alloc.Allocate();
                fontObjs[font] = type0;
                cidFontObjs[font] = (type0, cidFont, descriptor, stream, toUnicode);
            }
            else
            {
                fontObjs[font] = alloc.Allocate();
            }
        }

        var allExtGStates = new HashSet<string>();
        foreach (var page in _pages)
            foreach (var gs in page.UsedExtGStates)
                allExtGStates.Add(gs);
        var extGStateObjs = new Dictionary<string, int>();
        foreach (var gs in allExtGStates)
            extGStateObjs[gs] = alloc.Allocate();

        var imageObjs = new Dictionary<string, int>();
        var imageSMaskObjs = new Dictionary<string, int>();
        foreach (var kv in _images)
        {
            imageObjs[kv.Key] = alloc.Allocate();
            if (kv.Value.SMaskData != null)
                imageSMaskObjs[kv.Key] = alloc.Allocate();
        }

        int infoObj = alloc.Allocate();

        int outlineRootObj = 0;
        var outlineItemObjs = new List<int>();
        if (_bookmarks != null && _bookmarks.Count > 0)
        {
            outlineRootObj = alloc.Allocate();
            for (int i = 0; i < _bookmarks.Count; i++)
                outlineItemObjs.Add(alloc.Allocate());
        }

        var (iccProfileObj, metadataObj) = AllocateConformanceObjects(alloc);

        var (facturXEmbeddedFileObj, facturXFilespecObj) = AllocateFacturXObjects(alloc);

        LinkAnnotationsToStructureTree(alloc);

        AllocateStructureObjects(alloc);

        alloc.RecordOffset(catalogObj, writer.Position);
        writer.WriteLine($"{catalogObj} 0 obj");
        var catalogDict = new StringBuilder();
        catalogDict.Append("<< /Type /Catalog");
        catalogDict.Append($" /Pages {pagesObj} 0 R");
        if (outlineRootObj > 0)
            catalogDict.Append($" /Outlines {outlineRootObj} 0 R");
        AppendConformanceCatalogEntries(catalogDict, iccProfileObj, metadataObj);
        AppendFacturXCatalogEntries(catalogDict, facturXFilespecObj);
        AppendStructureCatalogEntries(catalogDict);
        catalogDict.Append(" >>");
        writer.WriteLine(catalogDict.ToString());
        writer.WriteLine("endobj");

        alloc.RecordOffset(pagesObj, writer.Position);
        writer.WriteLine($"{pagesObj} 0 obj");
        var kids = string.Join(" ", pageObjs.ConvertAll(p => $"{p.pageDict} 0 R"));
        writer.WriteLine($"<< /Type /Pages /Kids [{kids}] /Count {_pages.Count} >>");
        writer.WriteLine("endobj");

        foreach (var kv in fontObjs)
        {
            if (cidFontObjs.TryGetValue(kv.Key, out var cid))
            {
                WriteCIDFont(writer, alloc, kv.Key, cid, _embeddedFonts[kv.Key], enc);
            }
            else
            {
                alloc.RecordOffset(kv.Value, writer.Position);
                writer.WriteLine($"{kv.Value} 0 obj");
                writer.WriteLine($"<< /Type /Font /Subtype /Type1 /BaseFont /{kv.Key} /Encoding /WinAnsiEncoding >>");
                writer.WriteLine("endobj");
            }
        }

        foreach (var kv in extGStateObjs)
        {
            alloc.RecordOffset(kv.Value, writer.Position);
            writer.WriteLine($"{kv.Value} 0 obj");

            if (kv.Key.StartsWith("GSBM_"))
            {
                var cssName = kv.Key.Substring(5).Replace('_', '-');
                var pdfBm = PdfPage.CssBlendModeToPdf(cssName) ?? "Normal";
                writer.WriteLine($"<< /Type /ExtGState /BM /{pdfBm} >>");
            }
            else
            {
                float opacity = 1.0f;
                if (kv.Key.StartsWith("GS") && int.TryParse(kv.Key.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pct))
                    opacity = pct / 100f;
                writer.WriteLine($"<< /Type /ExtGState /ca {F(opacity)} /CA {F(opacity)} >>");
            }

            writer.WriteLine("endobj");
        }

        foreach (var kv in imageSMaskObjs)
        {
            var img = _images[kv.Key];
            var smaskData = img.SMaskData!;

            byte[] compressedSmask = EncryptBytes(enc, CompressZlib(smaskData), kv.Value);

            alloc.RecordOffset(kv.Value, writer.Position);
            writer.WriteLine($"{kv.Value} 0 obj");
            writer.WriteLine($"<< /Type /XObject /Subtype /Image /Width {img.Width} /Height {img.Height}");
            writer.WriteLine($"/ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode /Length {compressedSmask.Length} >>");
            writer.WriteLine("stream");
            writer.WriteBytes(compressedSmask);
            writer.WriteLine("");
            writer.WriteLine("endstream");
            writer.WriteLine("endobj");
        }

        foreach (var kv in imageObjs)
        {
            var img = _images[kv.Key];

            alloc.RecordOffset(kv.Value, writer.Position);
            writer.WriteLine($"{kv.Value} 0 obj");

            if (img.Format == PdfImageFormat.Jpeg)
            {
                byte[] jpegBody = EncryptBytes(enc, img.Data, kv.Value);
                var imgDict = new StringBuilder();
                imgDict.Append($"<< /Type /XObject /Subtype /Image /Width {img.Width} /Height {img.Height}");
                imgDict.Append($" /ColorSpace {img.ColorSpace} /BitsPerComponent {img.BitsPerComponent}{img.DecodeEntry}");
                imgDict.Append($" /Filter /DCTDecode /Length {jpegBody.Length}");
                imgDict.Append(" >>");
                writer.WriteLine(imgDict.ToString());
                writer.WriteLine("stream");
                writer.WriteBytes(jpegBody);
                writer.WriteLine("");
                writer.WriteLine("endstream");
            }
            else
            {
                byte[] compressed = EncryptBytes(enc, CompressZlib(img.Data), kv.Value);

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

        var fontResources = new StringBuilder();
        fontResources.Append("<< ");
        foreach (var kv in fontObjs)
            fontResources.Append($"/{kv.Key} {kv.Value} 0 R ");
        fontResources.Append(">>");

        var xobjectResources = new StringBuilder();
        if (imageObjs.Count > 0)
        {
            xobjectResources.Append("<< ");
            foreach (var kv in imageObjs)
                xobjectResources.Append($"/{kv.Key} {kv.Value} 0 R ");
            xobjectResources.Append(">>");
        }

        for (int i = 0; i < _pages.Count; i++)
        {
            var page = _pages[i];
            var (pageDictObj, contentStreamObj) = pageObjs[i];

            var contentBytes = Latin1Encoding.GetBytes(page.ContentStream.ToString());

            alloc.RecordOffset(contentStreamObj, writer.Position);
            writer.WriteLine($"{contentStreamObj} 0 obj");
            if (CompressContentStreams)
            {
                var compressedContent = EncryptBytes(enc, CompressZlib(contentBytes), contentStreamObj);
                writer.WriteLine($"<< /Length {compressedContent.Length} /Filter /FlateDecode >>");
                writer.WriteLine("stream");
                writer.WriteBytes(compressedContent);
            }
            else
            {
                var contentBody = EncryptBytes(enc, contentBytes, contentStreamObj);
                writer.WriteLine($"<< /Length {contentBody.Length} >>");
                writer.WriteLine("stream");
                writer.WriteBytes(contentBody);
            }
            writer.WriteLine("");
            writer.WriteLine("endstream");
            writer.WriteLine("endobj");

            alloc.RecordOffset(pageDictObj, writer.Position);
            writer.WriteLine($"{pageDictObj} 0 obj");
            var pageDict = new StringBuilder();
            pageDict.Append("<< /Type /Page");
            pageDict.Append($" /Parent {pagesObj} 0 R");
            pageDict.Append($" /MediaBox [0 0 {F(page.WidthPt)} {F(page.HeightPt)}]");
            pageDict.Append($" /Contents {contentStreamObj} 0 R");
            if (StructureTree != null)
            {
                pageDict.Append(" /Tabs /S");
                if (page.HasMarkedContent)
                    pageDict.Append($" /StructParents {page.PageIndex}");
            }
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

            if (page.Links.Count > 0)
            {
                pageDict.Append(" /Annots [");
                foreach (var link in page.Links)
                {
                    if (link.AnnotObj != 0)
                    {
                        pageDict.Append($" {link.AnnotObj} 0 R");
                    }
                    else
                    {
                        float x1 = link.X;
                        float y1 = link.Y;
                        float x2 = link.X + link.Width;
                        float y2 = link.Y + link.Height;
                        pageDict.Append($" << /Type /Annot /Subtype /Link /Rect [{F(x1)} {F(y1)} {F(x2)} {F(y2)}]");
                        pageDict.Append($" /Border [0 0 0]");
                        pageDict.Append(LinkTarget(link, enc, pageDictObj, pageObjs));
                        pageDict.Append(" >>");
                    }
                }
                pageDict.Append(" ]");
            }

            pageDict.Append(" >>");
            writer.WriteLine(pageDict.ToString());
            writer.WriteLine("endobj");

            foreach (var link in page.Links)
            {
                if (link.AnnotObj == 0) continue;
                float x1 = link.X;
                float y1 = link.Y;
                float x2 = link.X + link.Width;
                float y2 = link.Y + link.Height;
                alloc.RecordOffset(link.AnnotObj, writer.Position);
                writer.WriteLine($"{link.AnnotObj} 0 obj");
                var annotDict = new StringBuilder();
                annotDict.Append($"<< /Type /Annot /Subtype /Link /Rect [{F(x1)} {F(y1)} {F(x2)} {F(y2)}]");
                annotDict.Append(" /Border [0 0 0]");
                annotDict.Append(LinkTarget(link, enc, link.AnnotObj, pageObjs));
                annotDict.Append($" /StructParent {link.StructParentKey}");
                annotDict.Append(" >>");
                writer.WriteLine(annotDict.ToString());
                writer.WriteLine("endobj");
            }
        }

        if (_bookmarks != null && _bookmarks.Count > 0 && outlineRootObj > 0)
        {
            WriteOutlineObjects(writer, alloc, pageObjs, outlineRootObj, outlineItemObjs, enc);
        }

        alloc.RecordOffset(infoObj, writer.Position);
        writer.WriteLine($"{infoObj} 0 obj");
        var info = new StringBuilder();
        info.Append($"<< /Producer {PdfString(enc, "EggPdf", infoObj)}");
        if (!string.IsNullOrEmpty(Title))
            info.Append($" /Title {PdfString(enc, Title!, infoObj)}");
        if (!string.IsNullOrEmpty(Author))
            info.Append($" /Author {PdfString(enc, Author!, infoObj)}");
        info.Append($" /CreationDate {PdfString(enc, PdfDates.PdfDate(DateTime.UtcNow), infoObj)}");
        info.Append(" >>");
        writer.WriteLine(info.ToString());
        writer.WriteLine("endobj");

        WriteConformanceObjects(writer, alloc, iccProfileObj, metadataObj);

        WriteFacturXObjects(writer, alloc, facturXEmbeddedFileObj, facturXFilespecObj);

        WriteStructureObjects(writer, alloc, pageObjs);

        long xrefOffset = writer.Position;
        int totalObjects = alloc.Count + 1;
        writer.WriteLine("xref");
        writer.WriteLine($"0 {totalObjects}");
        writer.WriteLine("0000000000 65535 f ");

        for (int obj = 1; obj < totalObjects; obj++)
        {
            if (alloc.TryGetOffset(obj, out long offset))
                writer.WriteLine($"{offset:D10} 00000 n ");
            else
                writer.WriteLine("0000000000 00000 f ");
        }

        writer.WriteLine("trailer");
        var trailerDict = new StringBuilder();
        trailerDict.Append($"<< /Size {totalObjects} /Root {catalogObj} 0 R /Info {infoObj} 0 R");

        if (encDocId.Length > 0)
        {
            string idHex = BitConverter.ToString(encDocId).Replace("-", "");
            trailerDict.Append($" /ID [<{idHex}> <{idHex}>]");
        }

        if (enc != null)
        {
            var encParams = enc;

            string oHex = BitConverter.ToString(encParams.OValue).Replace("-", "");
            string uHex = BitConverter.ToString(encParams.UValue).Replace("-", "");

            trailerDict.Append($" /Encrypt << /Filter /Standard /V 2 /R 3 /Length {encParams.KeyLength}");
            trailerDict.Append($" /P {encParams.Permissions.ToString(CultureInfo.InvariantCulture)}");
            trailerDict.Append($" /O <{oHex}>");
            trailerDict.Append($" /U <{uHex}>");
            trailerDict.Append(" >>");
        }

        trailerDict.Append(" >>");
        writer.WriteLine(trailerDict.ToString());
        writer.WriteLine("startxref");
        writer.WriteLine(xrefOffset.ToString(CultureInfo.InvariantCulture));
        writer.WriteLine("%%EOF");
    }

    private void WriteOutlineObjects(
        PdfStreamWriter writer,
        PdfObjectAllocator alloc,
        List<(int pageDict, int contentStream)> pageObjs,
        int outlineRootObj,
        List<int> outlineItemObjs,
        EncryptionParams? enc)
    {
        int count = _bookmarks!.Count;
        var parentObj = new int[count];
        var firstChild = new int[count];
        var lastChild = new int[count];
        var prevSibling = new int[count];
        var nextSibling = new int[count];
        var childCount = new int[count];

        for (int i = 0; i < count; i++)
        {
            parentObj[i] = outlineRootObj;
            firstChild[i] = -1;
            lastChild[i] = -1;
            prevSibling[i] = -1;
            nextSibling[i] = -1;
        }

        var ancestorStack = new List<int>();

        var topLevelChildren = new List<int>();

        for (int i = 0; i < count; i++)
        {
            int level = _bookmarks[i].Level;

            while (ancestorStack.Count > 0)
            {
                int ancestorIdx = ancestorStack[ancestorStack.Count - 1];
                if (_bookmarks[ancestorIdx].Level < level)
                    break;
                ancestorStack.RemoveAt(ancestorStack.Count - 1);
            }

            if (ancestorStack.Count > 0)
            {
                int parentIdx = ancestorStack[ancestorStack.Count - 1];
                parentObj[i] = outlineItemObjs[parentIdx];

                if (firstChild[parentIdx] == -1)
                {
                    firstChild[parentIdx] = i;
                }
                else
                {
                    int prevIdx = lastChild[parentIdx];
                    nextSibling[prevIdx] = i;
                    prevSibling[i] = prevIdx;
                }
                lastChild[parentIdx] = i;
            }
            else
            {
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

        for (int i = 0; i < count; i++)
            childCount[i] = CountDescendants(i, firstChild, nextSibling);

        int rootFirst = topLevelChildren.Count > 0 ? topLevelChildren[0] : 0;
        int rootLast = topLevelChildren.Count > 0 ? topLevelChildren[topLevelChildren.Count - 1] : 0;
        int totalTopLevel = topLevelChildren.Count;

        alloc.RecordOffset(outlineRootObj, writer.Position);
        writer.WriteLine($"{outlineRootObj} 0 obj");
        writer.WriteLine($"<< /Type /Outlines /First {outlineItemObjs[rootFirst]} 0 R /Last {outlineItemObjs[rootLast]} 0 R /Count {totalTopLevel} >>");
        writer.WriteLine("endobj");

        for (int i = 0; i < count; i++)
        {
            var bm = _bookmarks[i];
            int objNum = outlineItemObjs[i];

            int pageIdx = Math.Min(bm.PageIndex, pageObjs.Count - 1);
            if (pageIdx < 0) pageIdx = 0;
            int pageRef = pageObjs[pageIdx].pageDict;

            var entry = new StringBuilder();
            entry.Append("<< /Title ");
            entry.Append(PdfString(enc, bm.Title, objNum));
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

            alloc.RecordOffset(objNum, writer.Position);
            writer.WriteLine($"{objNum} 0 obj");
            writer.WriteLine(entry.ToString());
            writer.WriteLine("endobj");
        }
    }

    private static int CountDescendants(int index, int[] firstChild, int[] nextSibling)
    {
        int total = 0;
        int child = firstChild[index];
        while (child >= 0)
        {
            total++;
            total += CountDescendants(child, firstChild, nextSibling);
            child = nextSibling[child];
        }
        return total;
    }

    internal static byte[] CompressZlib(byte[] data)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78);
        ms.WriteByte(0x9C);
        using (var ds = new DeflateStream(ms, CompressionLevel.Fastest, true))
            ds.Write(data, 0, data.Length);

        uint a = 1, b = 0;
        for (int i = 0; i < data.Length; i++)
        {
            a = (a + data[i]) % 65521;
            b = (b + a) % 65521;
        }
        uint adler = (b << 16) | a;
        ms.WriteByte((byte)(adler >> 24));
        ms.WriteByte((byte)(adler >> 16));
        ms.WriteByte((byte)(adler >> 8));
        ms.WriteByte((byte)adler);

        return ms.ToArray();
    }

    private void WriteCIDFont(PdfStreamWriter writer, PdfObjectAllocator alloc,
        string fontName, (int type0, int cidFont, int descriptor, int stream, int toUnicode) objs,
        EmbeddedFontData fontData, EncryptionParams? enc)
    {
        byte[] compressed = EncryptBytes(enc, CompressZlib(fontData.SubsetData), objs.stream);

        var wArray = new StringBuilder();
        wArray.Append("[0 [");
        for (int i = 0; i < fontData.Widths.Length; i++)
        {
            if (i > 0) wArray.Append(' ');
            int w = fontData.UnitsPerEm > 0 ? (int)(fontData.Widths[i] * 1000L / fontData.UnitsPerEm) : fontData.Widths[i];
            wArray.Append(w);
        }
        wArray.Append("]]");

        byte[] toUnicodeData = BuildToUnicodeCMap(fontData.CodepointToGlyphId);

        byte[] toUnicodeCompressed = EncryptBytes(enc, CompressZlib(toUnicodeData), objs.toUnicode);

        alloc.RecordOffset(objs.stream, writer.Position);
        writer.WriteLine($"{objs.stream} 0 obj");
        writer.WriteLine($"<< /Length {compressed.Length} /Length1 {fontData.SubsetData.Length} /Filter /FlateDecode >>");
        writer.WriteLine("stream");
        writer.WriteBytes(compressed);
        writer.WriteLine("");
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");

        string ascent = (fontData.UnitsPerEm > 0 ? fontData.Ascent * 1000 / fontData.UnitsPerEm : fontData.Ascent).ToString(CultureInfo.InvariantCulture);
        string descent = (fontData.UnitsPerEm > 0 ? fontData.Descent * 1000 / fontData.UnitsPerEm : fontData.Descent).ToString(CultureInfo.InvariantCulture);
        int flags = 32;

        alloc.RecordOffset(objs.descriptor, writer.Position);
        writer.WriteLine($"{objs.descriptor} 0 obj");
        writer.WriteLine($"<< /Type /FontDescriptor /FontName /{fontName}");
        writer.WriteLine($"/Flags {flags} /Ascent {ascent} /Descent {descent}");
        writer.WriteLine($"/ItalicAngle 0 /CapHeight {ascent} /StemV 80");
        writer.WriteLine($"/FontBBox [0 {descent} 1000 {ascent}]");
        writer.WriteLine($"/FontFile2 {objs.stream} 0 R >>");
        writer.WriteLine("endobj");

        alloc.RecordOffset(objs.cidFont, writer.Position);
        writer.WriteLine($"{objs.cidFont} 0 obj");
        writer.WriteLine($"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{fontName}");
        writer.WriteLine($"/CIDSystemInfo << /Registry {PdfString(enc, "Adobe", objs.cidFont)} /Ordering {PdfString(enc, "Identity", objs.cidFont)} /Supplement 0 >>");
        writer.WriteLine($"/FontDescriptor {objs.descriptor} 0 R");
        writer.WriteLine($"/W {wArray}");
        writer.WriteLine($"/DW 1000 >>");
        writer.WriteLine("endobj");

        alloc.RecordOffset(objs.toUnicode, writer.Position);
        writer.WriteLine($"{objs.toUnicode} 0 obj");
        writer.WriteLine($"<< /Length {toUnicodeCompressed.Length} /Filter /FlateDecode >>");
        writer.WriteLine("stream");
        writer.WriteBytes(toUnicodeCompressed);
        writer.WriteLine("");
        writer.WriteLine("endstream");
        writer.WriteLine("endobj");

        alloc.RecordOffset(objs.type0, writer.Position);
        writer.WriteLine($"{objs.type0} 0 obj");
        writer.WriteLine($"<< /Type /Font /Subtype /Type0 /BaseFont /{fontName}");
        writer.WriteLine($"/Encoding /Identity-H");
        writer.WriteLine($"/DescendantFonts [{objs.cidFont} 0 R]");
        writer.WriteLine($"/ToUnicode {objs.toUnicode} 0 R >>");
        writer.WriteLine("endobj");
    }

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

        var gidToCodepoint = new Dictionary<ushort, int>();
        foreach (var kv in codepointToGid)
        {
            if (!gidToCodepoint.ContainsKey(kv.Value))
                gidToCodepoint[kv.Value] = kv.Key;
        }

        var entries = new KeyValuePair<ushort, int>[gidToCodepoint.Count];
        int ei = 0;
        foreach (var kv in gidToCodepoint) entries[ei++] = kv;
        Array.Sort(entries, (a, b) => a.Key.CompareTo(b.Key));
        int idx = 0;
        while (idx < entries.Length)
        {
            int batchSize = Math.Min(100, entries.Length - idx);
            sb.AppendLine($"{batchSize} beginbfchar");
            for (int i = 0; i < batchSize; i++)
            {
                var entry = entries[idx + i];
                if (entry.Value <= 0xFFFF)
                    sb.AppendLine($"<{entry.Key:X4}> <{entry.Value:X4}>");
                else
                {
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

    private static byte[] EncryptBytes(EncryptionParams? enc, byte[] data, int objNum)
        => enc == null ? data : PdfEncryption.EncryptForObject(enc.EncryptionKey, objNum, 0, data);

    private readonly Dictionary<string, (int PageIndex, float TopPt)> _anchors = new(StringComparer.Ordinal);

    private static bool IsDocumentTop(string id)
        => id.Length == 0 || string.Equals(id, "top", StringComparison.OrdinalIgnoreCase);

    private string LinkTarget(PdfLinkAnnotation link, EncryptionParams? enc, int objNum, List<(int pageDict, int contentStream)> pageObjs)
    {
        if (link.TargetId != null)
        {
            if (_anchors.TryGetValue(link.TargetId, out var anchor))
            {
                int pageIdx = Math.Min(anchor.PageIndex, pageObjs.Count - 1);
                return $" /Dest [{pageObjs[pageIdx].pageDict} 0 R /XYZ 0 {F(anchor.TopPt)} 0]";
            }
            return $" /Dest [{pageObjs[0].pageDict} 0 R /Fit]";
        }
        return $" /A << /Type /Action /S /URI /URI {PdfString(enc, link.Url, objNum)} >>";
    }

    /// <summary>
    /// Register the destination of an HTML element <c>id</c>: the page it was painted on and its top
    /// edge in PDF points. The first registration of an id wins, matching browsers resolving
    /// <c>#id</c> to the first element carrying it. Internal links (<see cref="PdfPage.AddInternalLink"/>)
    /// resolve against these when the file is written.
    /// </summary>
    public void RegisterAnchor(string id, int pageIndex, float topPt)
    {
        if (!_anchors.ContainsKey(id))
            _anchors[id] = (pageIndex, topPt);
    }

    private static string PdfString(EncryptionParams? enc, string text, int objNum)
    {
        if (enc == null) return "(" + EscapePdfString(text) + ")";

        var cipher = PdfEncryption.EncryptForObject(enc.EncryptionKey, objNum, 0, Latin1Encoding.GetBytes(text));
        var sb = new StringBuilder(cipher.Length * 2 + 2);
        sb.Append('<');
        for (int i = 0; i < cipher.Length; i++) sb.Append(cipher[i].ToString("X2"));
        sb.Append('>');
        return sb.ToString();
    }
}

internal class EmbeddedFontData
{
    public byte[] SubsetData { get; set; } = Array.Empty<byte>();
    public Dictionary<int, ushort> CodepointToGlyphId { get; set; } = new();
    public ushort[] Widths { get; set; } = Array.Empty<ushort>();
    public int UnitsPerEm { get; set; }
    public int Ascent { get; set; }
    public int Descent { get; set; }
    public Dictionary<int, List<(ushort newGlyphId, float r, float g, float b, bool useTextColor)>>? ColorLayers { get; set; }
    public EggPdf.Text.TrueType.FontData? ShapingFont { get; set; }
    public Dictionary<ushort, ushort>? OldToNewGlyphId { get; set; }
}

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
