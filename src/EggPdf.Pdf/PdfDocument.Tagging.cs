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

    /// <summary>
    /// Optional structure tree root. When set, the document is tagged: <see cref="PdfPage.BeginMarkedContent"/>
    /// calls made while painting must have their (page, MCID) pairs registered against tree nodes via
    /// <see cref="PdfStructureElement.AddContentRef"/> for the tagging to actually link to content.
    /// </summary>
    public PdfStructureElement? StructureTree { get; set; }

    /// <summary>Document language for /Lang (e.g. "en-US"), from the HTML's own <c>lang</c> attribute. Defaults to "en" when tagging is on and none was set.</summary>
    public string? DocumentLanguage { get; set; }

    /// <summary>Reserve object numbers for StructTreeRoot, the ParentTree, and every structure element in the tree (a no-op unless <see cref="StructureTree"/> is set).</summary>
    private void AllocateStructureObjects(PdfObjectAllocator alloc)
    {
        if (StructureTree == null) return;
        _structTreeRootObj = alloc.Allocate();
        _parentTreeObj = alloc.Allocate();
        AllocateStructElemObjectsRecursive(StructureTree, alloc);
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

        WriteStructElemRecursive(writer, alloc, StructureTree, _structTreeRootObj, pageObjs, parentTreeEntries);

        int nextKey = 0;
        foreach (var pageIdx in parentTreeEntries.Keys)
            if (pageIdx >= nextKey) nextKey = pageIdx + 1;

        alloc.RecordOffset(_structTreeRootObj, writer.Position);
        writer.WriteLine($"{_structTreeRootObj} 0 obj");
        writer.WriteLine($"<< /Type /StructTreeRoot /K [{_structElemObjs[StructureTree]} 0 R] /ParentTree {_parentTreeObj} 0 R /ParentTreeNextKey {nextKey} >>");
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
        nums.Append("] >>");
        writer.WriteLine(nums.ToString());
        writer.WriteLine("endobj");
    }

    private void WriteStructElemRecursive(PdfStreamWriter writer, PdfObjectAllocator alloc, PdfStructureElement elem, int parentObj,
        List<(int pageDict, int contentStream)> pageObjs, SortedDictionary<int, SortedDictionary<int, int>> parentTreeEntries)
    {
        int elemObj = _structElemObjs[elem];

        var kArray = new StringBuilder();
        kArray.Append('[');
        foreach (var kid in elem.Kids)
        {
            if (kid.Element != null)
            {
                WriteStructElemRecursive(writer, alloc, kid.Element, elemObj, pageObjs, parentTreeEntries);
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
        kArray.Append(']');

        alloc.RecordOffset(elemObj, writer.Position);
        writer.WriteLine($"{elemObj} 0 obj");
        var dict = new StringBuilder();
        dict.Append("<< /Type /StructElem");
        dict.Append($" /S /{elem.Type}");
        dict.Append($" /P {parentObj} 0 R");
        dict.Append($" /K {kArray}");
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
