namespace EggPdf.Pdf;

/// <summary>
/// Which PDF/UA tagging specification version to target when <see cref="PdfDocument.StructureTree"/>
/// is set. <see cref="Ua1"/> (the default) is ISO 14289-1, based on PDF 1.7 -- the version EggPdf
/// has supported since PDF/UA-1 tagging was first built. <see cref="Ua2"/> is ISO 14289-2:2024,
/// based on WTPDF and PDF 2.0: it requires a <c>%PDF-2.0</c> header and PDF 2.0 structure namespace
/// declarations on the structure tree, on top of the same StructTreeRoot/ParentTree/MCID machinery
/// Ua1 already uses.
/// </summary>
public enum PdfUaVersion
{
    Ua1 = 1,
    Ua2 = 2,
}
