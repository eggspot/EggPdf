using System.Runtime.CompilerServices;

namespace EggPdf.Tests.Fluent;

/// <summary>Assembly-wide test configuration, run before any test executes.</summary>
internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Init()
    {
        // Same reasoning as EggPdf.Tests.Unit's TestSetup: these tests assert against raw
        // content-stream text ("(word) Tj" etc.), so keep the uncompressed layout here too.
        EggPdf.Pdf.PdfDocument.DefaultCompressContentStreams = false;
    }
}
