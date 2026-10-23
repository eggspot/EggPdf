namespace EggPdf.Pdf;

internal interface IPdfStreamLocator
{
    PdfByteSource Source { get; }

    bool EndstreamAt(long position);

    bool TryFindDataEnd(long dataStart, long end, out long dataLength);
}
