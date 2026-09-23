using System.Collections.Generic;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// PDF/UA-1 tagged-PDF structure tree support for <see cref="PdfDocument"/>: StructTreeRoot,
/// StructElem objects, and the ParentTree that links marked content back to them, kept separate
/// from the core writer in <c>PdfDocument.cs</c>.
/// </summary>
public partial class PdfDocument
{
    private readonly Dictionary<PdfStructureElement, int> _structElemObjs = new();
    private int _structTreeRootObj;
    private int _parentTreeObj;
    private int _pdf2NamespaceObj;

    /// <summary>The PDF 2.0 standard structure namespace URI (ISO 32000-2), declared on StructTreeRoot and referenced by every StructElem's /NS under PDF/UA-2.</summary>
    private const string Pdf2StructureNamespaceUri = "http://iso.org/pdf2/ssn";

    /// <summary>
    /// Custom (non-standard) structure types this MVP's HTML5 landmark tagging emits, mapped to
    /// the nearest ISO 32000-1 standard type. PDF 1.7 -- what PDF/UA-1 is based on -- has no native
    /// Nav/Header/Footer/Aside/Main types; the tagged-PDF architecture's own answer to that (ISO
    /// 32000-1 14.7.5, "RoleMap") is exactly this: declare a custom type and map it to a standard
    /// fallback so a reader/tool that doesn't recognize the custom name still gets correct-enough
    /// semantics. Always written when tagged, whether or not a given landmark tag is actually used.
    /// </summary>
    private static readonly Dictionary<string, string> LandmarkRoleMap = new()
    {
        ["Nav"] = "Div",
        ["Header"] = "Div",
        ["Footer"] = "Div",
        ["Aside"] = "Div",
        ["Main"] = "Div",
        ["Article"] = "Sect",
        ["Section"] = "Sect",
    };

    /// <summary>
    /// Optional structure tree root. When set, the document is tagged: <see cref="PdfPage.BeginMarkedContent"/>
    /// calls made while painting must have their (page, MCID) pairs registered against tree nodes via
    /// <see cref="PdfStructureElement.AddContentRef"/> for the tagging to actually link to content.
    /// </summary>
    public PdfStructureElement? StructureTree { get; set; }

    /// <summary>
    /// Which PDF/UA specification version tagging targets (only meaningful when
    /// <see cref="StructureTree"/> is set). Defaults to <see cref="PdfUaVersion.Ua1"/> (ISO
    /// 14289-1, PDF 1.7-based) for backward compatibility; <see cref="PdfUaVersion.Ua2"/> (ISO
    /// 14289-2:2024, PDF 2.0-based) switches the header to <c>%PDF-2.0</c>, declares the PDF 2.0
    /// structure namespace on every structure element, and resolves landmark regions straight to
    /// their standard fallback type instead of a custom name + RoleMap.
    /// </summary>
    public PdfUaVersion UaVersion { get; set; } = PdfUaVersion.Ua1;

    /// <summary>Document language for /Lang (e.g. "en-US"), from the HTML's own <c>lang</c> attribute. Defaults to "en" when tagging is on and none was set.</summary>
    public string? DocumentLanguage { get; set; }

    /// <summary>Reserve object numbers for StructTreeRoot, the ParentTree, and every structure element in the tree (a no-op unless <see cref="StructureTree"/> is set).</summary>
    private void AllocateStructureObjects(PdfObjectAllocator alloc)
    {
        if (StructureTree == null) return;
        _structTreeRootObj = alloc.Allocate();
        _parentTreeObj = alloc.Allocate();
        if (UaVersion == PdfUaVersion.Ua2)
            _pdf2NamespaceObj = alloc.Allocate();
        AllocateStructElemObjectsRecursive(StructureTree, alloc);
    }

    /// <summary>
    /// When tagged, allocate an indirect object number for every <c>&lt;a&gt;</c> link annotation
    /// the paint layer attributed to a Link structure element (<see cref="PdfLinkAnnotation.TaggedElement"/>)
    /// and register an OBJR back-reference on that element via <see cref="PdfStructureElement.AddAnnotationRef"/>
    /// -- ISO 14289-1 7.18.1 requires this cross-reference; without it, a reader can tell the anchor
    /// text is a link by content alone but has no structural link to the clickable annotation. Must
    /// run before <see cref="AllocateStructElemObjectsRecursive"/> reads Kids/AnnotationRefs while
    /// writing. Each annotation's <c>/StructParent</c> key starts right after the last page index,
    /// so it shares the ParentTree's single number space with pages' <c>/StructParents</c> without
    /// colliding.
    /// </summary>
    private void LinkAnnotationsToStructureTree(PdfObjectAllocator alloc)
    {
        if (StructureTree == null) return;
        int nextStructParentKey = _pages.Count;
        foreach (var page in _pages)
        {
            foreach (var link in page.Links)
            {
                if (link.TaggedElement == null) continue;
                link.AnnotObj = alloc.Allocate();
                link.StructParentKey = nextStructParentKey++;
                link.TaggedElement.AddAnnotationRef(link.AnnotObj, link.StructParentKey);
            }
        }
    }

    private void AllocateStructElemObjectsRecursive(PdfStructureElement elem, PdfObjectAllocator alloc)
    {
        _structElemObjs[elem] = alloc.Allocate();
        foreach (var kid in elem.Kids)
            if (kid.Element != null)
                AllocateStructElemObjectsRecursive(kid.Element, alloc);
    }

    /// <summary>Append the catalog's /StructTreeRoot, /MarkInfo, /ViewerPreferences and /Lang entries (a no-op unless <see cref="StructureTree"/> is set).</summary>
    private void AppendStructureCatalogEntries(StringBuilder catalogDict)
    {
        if (StructureTree == null) return;
        catalogDict.Append($" /StructTreeRoot {_structTreeRootObj} 0 R");
        catalogDict.Append(" /MarkInfo << /Marked true >>");
        catalogDict.Append(" /ViewerPreferences << /DisplayDocTitle true >>");
        var lang = string.IsNullOrEmpty(DocumentLanguage) ? "en" : DocumentLanguage;
        catalogDict.Append($" /Lang ({EscapePdfString(lang!)})");
    }

    /// <summary>
    /// Write every structure element object, then StructTreeRoot and the ParentTree number tree
    /// that maps each page's /StructParents index + MCID back to the owning element (a no-op
    /// unless <see cref="StructureTree"/> is set).
    /// </summary>
    private void WriteStructureObjects(PdfStreamWriter writer, PdfObjectAllocator alloc, List<(int pageDict, int contentStream)> pageObjs)
    {
        if (StructureTree == null) return;

        // pageIndex -> (mcid -> owning structure element's object number), filled in while
        // writing each element's /K array so the ParentTree can be built from it afterward.
        var parentTreeEntries = new SortedDictionary<int, SortedDictionary<int, int>>();
        // Link annotations' /StructParent key -> owning element's object number -- a direct entry
        // (not an array), since unlike page content an annotation has exactly one owning element.
        var annotParentTreeEntries = new SortedDictionary<int, int>();

        WriteStructElemRecursive(writer, alloc, StructureTree, _structTreeRootObj, pageObjs, parentTreeEntries, annotParentTreeEntries);

        // PDF/UA-2's Namespace object, written before StructTreeRoot references it.
        if (_pdf2NamespaceObj != 0)
        {
            alloc.RecordOffset(_pdf2NamespaceObj, writer.Position);
            writer.WriteLine($"{_pdf2NamespaceObj} 0 obj");
            writer.WriteLine($"<< /Type /Namespace /NS ({Pdf2StructureNamespaceUri}) >>");
            writer.WriteLine("endobj");
        }

        int nextKey = 0;
        foreach (var pageIdx in parentTreeEntries.Keys)
            if (pageIdx >= nextKey) nextKey = pageIdx + 1;
        foreach (var key in annotParentTreeEntries.Keys)
            if (key >= nextKey) nextKey = key + 1;

        alloc.RecordOffset(_structTreeRootObj, writer.Position);
        writer.WriteLine($"{_structTreeRootObj} 0 obj");
        var rootDict = new StringBuilder();
        rootDict.Append("<< /Type /StructTreeRoot");
        rootDict.Append($" /K [{_structElemObjs[StructureTree]} 0 R]");
        rootDict.Append($" /ParentTree {_parentTreeObj} 0 R");
        rootDict.Append($" /ParentTreeNextKey {nextKey}");
        if (_pdf2NamespaceObj != 0)
        {
            // PDF/UA-2 (veraPDF 8.2.4-1): every structure element must belong to a declared
            // namespace -- landmark regions are resolved straight to their standard fallback type
            // (see StructureTreeBuilder's Ua2LandmarkFallback) rather than needing a custom-type
            // RoleMapNS entry, so a plain /RoleMap (PDF 1.7's simpler mechanism) isn't needed here.
            rootDict.Append($" /Namespaces [{_pdf2NamespaceObj} 0 R]");
        }
        else
        {
            var roleMap = new StringBuilder();
            roleMap.Append("<< ");
            foreach (var kv in LandmarkRoleMap)
                roleMap.Append('/').Append(kv.Key).Append(" /").Append(kv.Value).Append(' ');
            roleMap.Append(">>");
            rootDict.Append($" /RoleMap {roleMap}");
        }
        rootDict.Append(" >>");
        writer.WriteLine(rootDict.ToString());
        writer.WriteLine("endobj");

        alloc.RecordOffset(_parentTreeObj, writer.Position);
        writer.WriteLine($"{_parentTreeObj} 0 obj");
        var nums = new StringBuilder();
        nums.Append("<< /Nums [");
        foreach (var pageEntry in parentTreeEntries)
        {
            nums.Append(pageEntry.Key).Append(" [");
            int maxMcid = -1;
            foreach (var mcid in pageEntry.Value.Keys)
                if (mcid > maxMcid) maxMcid = mcid;
            for (int mcid = 0; mcid <= maxMcid; mcid++)
            {
                // Every MCID PdfPage hands out is registered against exactly one element by the
                // caller (see PdfStructureElement.AddContentRef); a gap here means a marked-content
                // span was opened but never attributed to a structure element.
                if (pageEntry.Value.TryGetValue(mcid, out int elemObj))
                    nums.Append(elemObj).Append(" 0 R ");
                else
                    nums.Append("null ");
            }
            nums.Append("] ");
        }
        // Annotation keys are all >= _pages.Count, i.e. strictly greater than every page key above,
        // so appending them after the page entries keeps /Nums in the ascending order a PDF number
        // tree requires without needing to interleave the two spaces.
        foreach (var annotEntry in annotParentTreeEntries)
            nums.Append(annotEntry.Key).Append(' ').Append(annotEntry.Value).Append(" 0 R ");
        nums.Append("] >>");
        writer.WriteLine(nums.ToString());
        writer.WriteLine("endobj");
    }

    private void WriteStructElemRecursive(PdfStreamWriter writer, PdfObjectAllocator alloc, PdfStructureElement elem, int parentObj,
        List<(int pageDict, int contentStream)> pageObjs, SortedDictionary<int, SortedDictionary<int, int>> parentTreeEntries,
        SortedDictionary<int, int> annotParentTreeEntries)
    {
        int elemObj = _structElemObjs[elem];

        var kArray = new StringBuilder();
        kArray.Append('[');
        foreach (var kid in elem.Kids)
        {
            if (kid.Element != null)
            {
                WriteStructElemRecursive(writer, alloc, kid.Element, elemObj, pageObjs, parentTreeEntries, annotParentTreeEntries);
                kArray.Append(_structElemObjs[kid.Element]).Append(" 0 R ");
            }
            else
            {
                if (!parentTreeEntries.TryGetValue(kid.PageIndex, out var mcidMap))
                {
                    mcidMap = new SortedDictionary<int, int>();
                    parentTreeEntries[kid.PageIndex] = mcidMap;
                }
                mcidMap[kid.Mcid] = elemObj;

                int pageObj = pageObjs[kid.PageIndex].pageDict;
                kArray.Append($"<< /Type /MCR /Pg {pageObj} 0 R /MCID {kid.Mcid} >> ");
            }
        }
        foreach (var annotRef in elem.AnnotationRefs)
        {
            annotParentTreeEntries[annotRef.StructParentKey] = elemObj;
            kArray.Append($"<< /Type /OBJR /Obj {annotRef.AnnotObj} 0 R >> ");
        }
        kArray.Append(']');

        alloc.RecordOffset(elemObj, writer.Position);
        writer.WriteLine($"{elemObj} 0 obj");
        var dict = new StringBuilder();
        dict.Append("<< /Type /StructElem");
        dict.Append($" /S /{elem.Type}");
        dict.Append($" /P {parentObj} 0 R");
        dict.Append($" /K {kArray}");
        if (_pdf2NamespaceObj != 0)
            dict.Append($" /NS {_pdf2NamespaceObj} 0 R");
        if (!string.IsNullOrEmpty(elem.Alt))
            dict.Append($" /Alt ({EscapePdfString(elem.Alt!)})");
        if (!string.IsNullOrEmpty(elem.Lang))
            dict.Append($" /Lang ({EscapePdfString(elem.Lang!)})");
        if (!string.IsNullOrEmpty(elem.TableScope))
            dict.Append($" /A << /O /Table /Scope /{elem.TableScope} >>");
        dict.Append(" >>");
        writer.WriteLine(dict.ToString());
        writer.WriteLine("endobj");
    }
}
