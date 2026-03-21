using System;
using System.IO;
using System.Threading.Tasks;

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
        bool verbose = HasFlag(args, "--verbose") || HasFlag(args, "-v");

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-o" || args[i] == "--output")
            {
                if (i + 1 < args.Length) outputPath = args[++i];
            }
            else if (!args[i].StartsWith("-"))
            {
                inputPath ??= args[i];
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

            if (outputPath == "-")
            {
                // Write to stdout
                var pdf = HtmlToPdf.Render(html);
                using var stdout = Console.OpenStandardOutput();
                await stdout.WriteAsync(pdf, 0, pdf.Length);
            }
            else
            {
                await HtmlToPdf.RenderToFileAsync(html, outputPath);
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
    -v, --verbose            Show render timing and file size
    --version                Show version
    -h, --help               Show this help

EXAMPLES:
    eggpdf report.html -o report.pdf
    eggpdf https://example.com -o page.pdf
    echo ""<h1>Hello</h1>"" | eggpdf - -o hello.pdf
    eggpdf input.html -o - > output.pdf

MORE INFO:
    https://github.com/eggspot/EggPdf");
    }
}
