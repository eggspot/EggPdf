using System.Runtime.CompilerServices;

namespace EggPdf.Tests.Unit;

/// <summary>
/// Assembly-wide test configuration, run before any test executes. Hundreds of tests assert against raw
/// content-stream text ("(word) Tj"), so page content stays uncompressed here; production compresses it by
/// default, and ContentStreamCompressionTests turns compression on per document.
/// </summary>
internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Init()
    {
        EggPdf.Pdf.PdfDocument.DefaultCompressContentStreams = false;
    }
}
