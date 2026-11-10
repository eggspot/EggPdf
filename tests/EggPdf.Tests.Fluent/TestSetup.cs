using System.Runtime.CompilerServices;

namespace EggPdf.Tests.Fluent;

/// <summary>
/// Assembly-wide test configuration, run before any test executes: content streams stay uncompressed, as in
/// EggPdf.Tests.Unit, because the tests assert against raw content-stream text ("(word) Tj").
/// </summary>
internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Init()
    {
        EggPdf.Pdf.PdfDocument.DefaultCompressContentStreams = false;
    }
}
