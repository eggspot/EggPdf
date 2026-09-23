using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using EggPdf.Pdf;

namespace EggPdf.Cli;

/// <summary>
/// EggPdf command-line tool: convert HTML to PDF/PNG from the terminal.
/// Usage: eggpdf input.html -o output.pdf [options]
/// </summary>
public class Program
{
    private const string Version = "0.1.0";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || HasFlag(args, "--help") || HasFlag(args, "-h"))
        {
            PrintUsage();
            return 0;
        }

        if (HasFlag(args, "--version"))
        {
            Console.WriteLine($"eggpdf {Version}");
            return 0;
        }

        // Parse arguments
        string? inputPath = null;
        string? outputPath = null;
        string? pdfaFlag = null;
        string? invoicePath = null;
        bool verbose = HasFlag(args, "--verbose") || HasFlag(args, "-v");
        bool tagged = HasFlag(args, "--tagged");

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-o" || args[i] == "--output")
            {
                if (i + 1 < args.Length) outputPath = args[++i];
            }
            else if (args[i] == "--pdfa")
            {
                if (i + 1 < args.Length) pdfaFlag = args[++i];
            }
            else if (args[i] == "--invoice")
            {
                if (i + 1 < args.Length) invoicePath = args[++i];
            }
            else if (!args[i].StartsWith("-"))
            {
                inputPath ??= args[i];
            }
        }

        PdfAConformance? conformance = null;
        if (pdfaFlag != null)
        {
            conformance = pdfaFlag.ToLowerInvariant() switch
            {
                "1b" => PdfAConformance.PdfA1b,
                "1a" => PdfAConformance.PdfA1a,
                "2b" => PdfAConformance.PdfA2b,
                "2u" => PdfAConformance.PdfA2u,
                "2a" => PdfAConformance.PdfA2a,
                "3b" => PdfAConformance.PdfA3b,
                "3u" => PdfAConformance.PdfA3u,
                "3a" => PdfAConformance.PdfA3a,
                _ => null,
            };
            if (conformance == null)
            {
                Console.Error.WriteLine($"Error: Invalid --pdfa value '{pdfaFlag}'. Expected one of: 1b, 1a, 2b, 2u, 2a, 3b, 3u, 3a.");
                return 1;
            }
            if (conformance.Value.RequiresTagging() && !tagged)
            {
                Console.Error.WriteLine($"Error: --pdfa {pdfaFlag} requires --tagged (level A conformance requires accessibility tagging).");
                return 1;
            }
        }

        FacturXInvoice? invoice = null;
        if (invoicePath != null)
        {
            if (conformance != PdfAConformance.PdfA3b && conformance != PdfAConformance.PdfA3u)
            {
                Console.Error.WriteLine("Error: --invoice requires --pdfa 3b or --pdfa 3u (Factur-X's embedded XML attachment is only permitted under PDF/A-3).");
                return 1;
            }
            if (!File.Exists(invoicePath))
            {
                Console.Error.WriteLine($"Error: Invoice file not found: {invoicePath}");
                return 1;
            }
            try
            {
                var invoiceJson = await File.ReadAllTextAsync(invoicePath);
                invoice = JsonSerializer.Deserialize<FacturXInvoice>(invoiceJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: Failed to parse invoice JSON: {ex.Message}");
                return 1;
            }
        }

        if (inputPath == null)
        {
            Console.Error.WriteLine("Error: No input file specified.");
            Console.Error.WriteLine("Usage: eggpdf input.html -o output.pdf");
            return 1;
        }

        // Default output
        outputPath ??= Path.ChangeExtension(
            inputPath == "-" ? "output" : inputPath, ".pdf");

        try
        {
            // Read HTML
            string html;
            if (inputPath == "-")
            {
                // Read from stdin
                html = await Console.In.ReadToEndAsync();
                if (verbose) Console.Error.WriteLine("Read HTML from stdin");
            }
            else if (inputPath.StartsWith("http://") || inputPath.StartsWith("https://"))
            {
                using var client = new System.Net.Http.HttpClient();
                html = await client.GetStringAsync(inputPath);
                if (verbose) Console.Error.WriteLine($"Fetched HTML from {inputPath}");
            }
            else
            {
                if (!File.Exists(inputPath))
                {
                    Console.Error.WriteLine($"Error: File not found: {inputPath}");
                    return 1;
                }
                html = await File.ReadAllTextAsync(inputPath);
                if (verbose) Console.Error.WriteLine($"Read {html.Length} chars from {inputPath}");
            }

            // Render
            var startTime = DateTime.UtcNow;

            PdfRenderOptions? options = (conformance != null || tagged)
                ? new PdfRenderOptions { Conformance = conformance, Invoice = invoice, Tagged = tagged }
                : null;
            var pdf = options != null ? HtmlToPdf.Render(html, options) : HtmlToPdf.Render(html);

            if (outputPath == "-")
            {
                using var stdout = Console.OpenStandardOutput();
                await stdout.WriteAsync(pdf, 0, pdf.Length);
            }
            else
            {
                await File.WriteAllBytesAsync(outputPath, pdf);
            }

            var elapsed = DateTime.UtcNow - startTime;

            if (verbose)
            {
                Console.Error.WriteLine($"Rendered in {elapsed.TotalMilliseconds:F0}ms -> {outputPath}");
                if (outputPath != "-" && File.Exists(outputPath))
                {
                    var size = new FileInfo(outputPath).Length;
                    Console.Error.WriteLine($"Output size: {size:N0} bytes");
                }
            }
            else if (outputPath != "-")
            {
                Console.WriteLine(outputPath);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (verbose) Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static bool HasFlag(string[] args, string flag)
    {
        foreach (var arg in args)
            if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static void PrintUsage()
    {
        Console.WriteLine($@"eggpdf {Version} - Pure C# HTML to PDF converter

USAGE:
    eggpdf <input> [options]

ARGUMENTS:
    <input>                  HTML file path, URL, or - for stdin

OPTIONS:
    -o, --output <path>      Output file path (default: input.pdf, or - for stdout)
    --pdfa <level>           PDF/A conformance: 1b, 1a, 2b, 2u, 2a, 3b, 3u, or 3a.
                              1b/1a throw if the document uses opacity, blend
                              modes, or images with alpha (PDF/A-1 forbids
                              transparency outright). Level A (1a/2a/3a)
                              requires --tagged (full accessibility conformance)
    --invoice <path>         ZUGFeRD/Factur-X invoice JSON (MINIMUM profile) to embed.
                              Requires --pdfa 3b or --pdfa 3u
    --tagged                 Produce a PDF/UA-1 tagged PDF (structure tree, alt
                              text, /Lang). Combinable with --pdfa. Not yet
                              supported with named page groups
    -v, --verbose            Show render timing and file size
    --version                Show version
    -h, --help               Show this help

EXAMPLES:
    eggpdf report.html -o report.pdf
    eggpdf https://example.com -o page.pdf
    echo ""<h1>Hello</h1>"" | eggpdf - -o hello.pdf
    eggpdf input.html -o - > output.pdf
    eggpdf report.html -o report.pdf --pdfa 2b
    eggpdf invoice.html -o invoice.pdf --pdfa 3b --invoice invoice-data.json
    eggpdf report.html -o report.pdf --tagged --pdfa 2b

INVOICE JSON (--invoice) FIELDS:
    invoiceNumber, issueDate, currencyCode, sellerName, sellerCountryCode,
    sellerVatId, buyerName, buyerReference, taxBasisTotal, taxTotal,
    grandTotal, duePayableAmount

MORE INFO:
    https://github.com/eggspot/EggPdf");
    }
}
