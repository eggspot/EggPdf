using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using EggPdf.Core.Resources;
using EggPdf.Pdf;

namespace EggPdf.Cli;

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

        string? inputPath = null;
        var optionValues = new Dictionary<string, string>(StringComparer.Ordinal);
        bool verbose = HasFlag(args, "--verbose") || HasFlag(args, "-v");
        bool tagged = HasFlag(args, "--tagged");
        bool noLocalFiles = HasFlag(args, "--no-local-files");
        bool noPrivateNetwork = HasFlag(args, "--no-private-network");

        for (int i = 0; i < args.Length; i++)
        {
            if (OptionsWithValue.Contains(args[i]))
            {
                if (i + 1 < args.Length) optionValues[args[i] == "--output" ? "-o" : args[i]] = args[++i];
            }
            else if (!args[i].StartsWith("-"))
            {
                inputPath ??= args[i];
            }
        }

        string? outputPath = optionValues.GetValueOrDefault("-o");
        string? pdfaFlag = optionValues.GetValueOrDefault("--pdfa");
        string? invoicePath = optionValues.GetValueOrDefault("--invoice");
        string? uaVersionFlag = optionValues.GetValueOrDefault("--ua-version");

        PdfUaVersion uaVersion = PdfUaVersion.Ua1;
        if (uaVersionFlag != null)
        {
            if (uaVersionFlag != "1" && uaVersionFlag != "2")
            {
                Console.Error.WriteLine($"Error: Invalid --ua-version value '{uaVersionFlag}'. Expected 1 or 2.");
                return 1;
            }
            if (!tagged)
            {
                Console.Error.WriteLine("Error: --ua-version requires --tagged.");
                return 1;
            }
            uaVersion = uaVersionFlag == "2" ? PdfUaVersion.Ua2 : PdfUaVersion.Ua1;
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
            if (uaVersion == PdfUaVersion.Ua2)
            {
                Console.Error.WriteLine("Error: --ua-version 2 cannot be combined with --pdfa -- there is no defined joint PDF/A + PDF/UA-2 standard. Use --ua-version 1 (the default) for a combined PDF/A + PDF/UA-1 document.");
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

        outputPath ??= Path.ChangeExtension(
            inputPath == "-" ? "output" : inputPath, ".pdf");

        try
        {
            string html;
            if (inputPath == "-")
            {
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

            using var signing = CliSigning.FromOptions(optionValues);

            var startTime = DateTime.UtcNow;

            var warnings = new EggPdf.Core.WarningCollector();
            var statistics = new RenderStatistics();
            var options = new PdfRenderOptions
            {
                Conformance = conformance, Invoice = invoice, Tagged = tagged, UaVersion = uaVersion, Warnings = warnings,
                AllowLocalFileAccess = !noLocalFiles, Statistics = statistics,
            };
            if (noPrivateNetwork)
            {
                bool remoteInput = inputPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                   inputPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
                if (remoteInput) options.AllowLocalFileAccess = false;
                var baseDirectory = inputPath == "-" || remoteInput
                    ? null
                    : Path.GetDirectoryName(Path.GetFullPath(inputPath));
                options.ResourceResolver = new CompositeResolver(baseDirectory);
            }
            var pdf = HtmlToPdf.Render(html, options);
            if (signing != null)
            {
                pdf = signing.Sign(pdf);
                if (verbose) Console.Error.WriteLine($"Signed as {signing.SignerName}");
            }

            foreach (var warning in warnings.Warnings)
                Console.Error.WriteLine($"Warning [{warning.Code}]: {warning.Message}");

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
                int pages = statistics.PageCount;
                Console.Error.WriteLine($"Rendered {pages.ToString(CultureInfo.InvariantCulture)} page{(pages == 1 ? "" : "s")} in {elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)}ms -> {outputPath}");
                if (outputPath != "-" && File.Exists(outputPath))
                {
                    var size = new FileInfo(outputPath).Length;
                    Console.Error.WriteLine($"Output size: {size.ToString("N0", CultureInfo.InvariantCulture)} bytes");
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

    private static readonly HashSet<string> OptionsWithValue = new(StringComparer.Ordinal)
    {
        "-o", "--output", "--pdfa", "--invoice", "--ua-version",
        CliSigning.PfxOption, CliSigning.PfxPasswordEnvOption, CliSigning.CertificateOption, CliSigning.KeyOption,
        CliSigning.KeyPasswordEnvOption, CliSigning.ReasonOption, CliSigning.LocationOption, CliSigning.NameOption,
    };

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
    --invoice <path>         ZUGFeRD/Factur-X invoice JSON to embed (MINIMUM profile,
                              or EN 16931/Comfort when the JSON's lineItems is
                              non-empty). Requires --pdfa 3b or --pdfa 3u
    --tagged                 Produce a tagged PDF (structure tree, alt text,
                              /Lang, landmark regions, Link OBJR cross-reference).
                              Combinable with --pdfa and with named page groups
    --ua-version <1|2>       PDF/UA spec version --tagged targets: 1 (default,
                              ISO 14289-1, PDF 1.7) or 2 (ISO 14289-2:2024,
                              PDF 2.0 -- cannot combine with --pdfa). Requires --tagged
    --no-local-files         Never read local files referenced by the HTML (paths,
                              file: URLs); only data: and http(s) resources load
    --no-private-network     Fetch http(s) resources (stylesheets, fonts, images)
                              safely: refuse loopback, private and cloud-metadata
                              addresses, re-check redirects, cap size and time.
                              Local files are then read only from the input
                              file's directory (stdin: the current directory;
                              URL input: none). Use both flags for untrusted HTML
    --sign-pfx <file>        Sign the PDF with this PFX/PKCS#12 certificate (RSA or ECDSA)
    --sign-password-env <VAR>
                             Read the PFX password from environment variable VAR
                              (never on the command line, where ps and shell history see it)
    --sign-cert <file>       Sign with a PEM certificate; needs --sign-key
    --sign-key <file>        PEM private key (PKCS#8, RSA, EC or encrypted PKCS#8)
    --sign-key-password-env <VAR>
                             Read an encrypted --sign-key's password from VAR
    --sign-reason <text>     Signature reason, e.g. ""Approved""
    --sign-location <text>   Signature location
    --sign-name <text>       Signer name (default: the certificate's subject name)
    -v, --verbose            Show page count, render timing and file size
    --version                Show version
    -h, --help               Show this help

EXAMPLES:
    eggpdf report.html -o report.pdf
    eggpdf https://example.com -o page.pdf
    echo ""<h1>Hello</h1>"" | eggpdf - -o hello.pdf
    eggpdf input.html -o - > output.pdf
    eggpdf report.html -o report.pdf --pdfa 2b
    eggpdf invoice.html -o invoice.pdf --pdfa 3b --invoice invoice-data.json
    eggpdf untrusted.html -o out.pdf --no-local-files --no-private-network
    PFX_PASS=... eggpdf contract.html -o signed.pdf --sign-pfx signer.pfx --sign-password-env PFX_PASS --sign-reason Approved
    eggpdf report.html -o report.pdf --tagged --pdfa 2b

INVOICE JSON (--invoice) FIELDS:
    invoiceNumber, issueDate, currencyCode, sellerName, sellerCountryCode,
    sellerVatId, buyerName, buyerReference, taxBasisTotal, taxTotal,
    grandTotal, duePayableAmount

MORE INFO:
    https://github.com/eggspot/EggPdf");
    }
}
