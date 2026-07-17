using System.Runtime.CompilerServices;

namespace EggPdf.Tests.Unit;

/// <summary>
/// Assembly-wide test configuration, run before any test executes.
/// </summary>
internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Init()
    {
        // Hundreds of tests assert against raw content-stream text
        // ("(word) Tj" etc.). Keep the legacy uncompressed layout for them;
        // compression has dedicated tests that enable it per document
        // (ContentStreamCompressionTests). Production default stays true.
        EggPdf.Pdf.PdfDocument.DefaultCompressContentStreams = false;
    }
}
