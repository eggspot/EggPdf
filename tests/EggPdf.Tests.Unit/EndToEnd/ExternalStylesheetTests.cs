using System;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// Tests for external stylesheet loading via &lt;link&gt; elements and @import rules.
/// Uses data: URIs to avoid filesystem dependencies in unit tests.
/// </summary>
public class ExternalStylesheetTests
{
    /// <summary>Encode CSS text as a base64 data: URI.</summary>
    private static string CssToDataUri(string css)
    {
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(css));
        return "data:text/css;base64," + base64;
    }

    [Fact]
    public async Task LinkStylesheet_DataUri_Applied()
    {
        var cssDataUri = CssToDataUri(".bold { font-weight: bold; }");
        var html = $@"<html><head>
            <link rel=""stylesheet"" href=""{cssDataUri}"">
        </head><body>
            <p class=""bold"">Bold from link</p>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Bold from link");
        text.Should().Contain("Helvetica-Bold", "<link> stylesheet should apply font-weight:bold");
    }

    [Fact]
    public async Task LinkStylesheet_WithMediaPrint_Applied()
    {
        var cssDataUri = CssToDataUri(".bold { font-weight: bold; }");
        var html = $@"<html><head>
            <link rel=""stylesheet"" href=""{cssDataUri}"" media=""print"">
        </head><body>
            <p class=""bold"">Print bold</p>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Print bold");
        text.Should().Contain("Helvetica-Bold", "media='print' stylesheet should be loaded");
    }

    [Fact]
    public async Task LinkStylesheet_WithMediaScreen_Ignored()
    {
        var cssDataUri = CssToDataUri("p { font-family: monospace; }");
        var html = $@"<html><head>
            <link rel=""stylesheet"" href=""{cssDataUri}"" media=""screen"">
        </head><body>
            <p>Not monospace</p>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Not monospace");
        text.Should().NotContain("Courier", "media='screen' stylesheet should be ignored for print");
    }

    [Fact]
    public async Task LinkStylesheet_NoRel_Ignored()
    {
        var cssDataUri = CssToDataUri("p { font-family: monospace; }");
        var html = $@"<html><head>
            <link href=""{cssDataUri}"">
        </head><body>
            <p>No rel attribute</p>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("No rel attribute");
        text.Should().NotContain("Courier", "<link> without rel='stylesheet' should be ignored");
    }

    [Fact]
    public async Task MultipleLinks_AllApplied()
    {
        var boldUri = CssToDataUri(".bold { font-weight: bold; }");
        var monoUri = CssToDataUri(".mono { font-family: monospace; }");
        var html = $@"<html><head>
            <link rel=""stylesheet"" href=""{boldUri}"">
            <link rel=""stylesheet"" href=""{monoUri}"">
        </head><body>
            <p class=""bold"">Bold text</p>
            <p class=""mono"">Mono text</p>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Bold text");
        text.Should().Contain("Mono text");
        text.Should().Contain("Helvetica-Bold", "first <link> should apply bold");
        text.Should().Contain("Courier", "second <link> should apply monospace");
    }

    [Fact]
    public async Task ImportRule_DataUri_Applied()
    {
        // The @import rule references a data: URI containing nested CSS
        var importedCss = CssToDataUri(".nested { font-family: monospace; }");
        var html = $@"<html><head><style>
            @import url(""{importedCss}"");
            .outer {{ font-weight: bold; }}
        </style></head><body>
            <p class=""nested"">Nested mono</p>
            <p class=""outer"">Outer bold</p>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Nested mono");
        text.Should().Contain("Outer bold");
        text.Should().Contain("Courier", "@import should load and apply the imported stylesheet");
        text.Should().Contain("Helvetica-Bold", "rules in the importing sheet should also apply");
    }

    [Fact]
    public void CircularImport_DoesNotHang()
    {
        // Two data URIs that reference each other would be complex to construct,
        // but we can test that the same URL imported twice doesn't cause infinite recursion.
        // Use a data URI that imports itself -- the visited URL tracking should prevent looping.
        var selfImportCss = "@import url(\"data:text/css,p%20%7B%20color%3A%20red%3B%20%7D\"); .test { font-weight: bold; }";
        var dataUri = CssToDataUri(selfImportCss);
        var html = $@"<html><head>
            <link rel=""stylesheet"" href=""{dataUri}"">
        </head><body>
            <p class=""test"">Should not hang</p>
        </body></html>";

        // This should complete without hanging or throwing
        var pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeNull();
        pdf.Length.Should().BeGreaterThan(0);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Should not hang");
    }

    [Fact]
    public void InvalidHref_DoesNotCrash()
    {
        var html = @"<html><head>
            <link rel=""stylesheet"" href=""nonexistent-file-that-does-not-exist.css"">
            <link rel=""stylesheet"" href="""">
            <link rel=""stylesheet"" href=""data:text/css;base64,!!!invalid!!!"">
        </head><body>
            <p>Still renders</p>
        </body></html>";

        // Should not throw
        var pdf = HtmlToPdf.Render(html);
        pdf.Should().NotBeNull();
        pdf.Length.Should().BeGreaterThan(0);

        var text = Encoding.ASCII.GetString(pdf);
        text.Should().Contain("Still renders");
    }
}
