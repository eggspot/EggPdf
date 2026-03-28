using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// End-to-end tests verifying CSS custom properties and calc() produce correct PDF output.
/// Full pipeline: HTML+CSS -> Parse -> Style -> Layout -> Paint -> PDF.
/// </summary>
public class CssVariablesE2ETests
{
    [Fact]
    public async Task VarInColor_AppliedInPdf()
    {
        var html = @"<html><head><style>
            :root { --brand-color: red; }
            p { color: var(--brand-color); }
        </style></head><body>
            <p>Brand colored text</p>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Brand colored text");
        // red = 1.00 0.00 0.00 rg in PDF (F2 formatting)
        text.Should().Contain("1.00 0.00 0.00 rg", "var(--brand-color) should resolve to red color");
    }

    [Fact]
    public async Task VarInFontSize_AffectsTextSize()
    {
        var html = @"<html><head><style>
            :root { --heading-size: 32px; }
            h1 { font-size: var(--heading-size); }
        </style></head><body>
            <h1>Large heading</h1>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Large heading");
        // 32px should appear as 24.00pt (32 * 72/96) in the PDF Tf operator
        text.Should().Contain("24.00 Tf", "var(--heading-size) should resolve to 32px = 24pt font size");
    }

    [Fact]
    public async Task CalcWidth_ProducesCorrectLayout()
    {
        var html = @"<div style='width: calc(200px - 20px); background-color: blue; height: 50px'>Calc box</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Calc box");
        text.Should().Contain("re", "calc width should produce a rectangle");
    }

    [Fact]
    public async Task VarWithFallback_UsedWhenUndefined()
    {
        var html = @"<html><head><style>
            p { color: var(--undefined-color, green); }
        </style></head><body>
            <p>Fallback colored text</p>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Fallback colored text");
    }

    [Fact]
    public async Task VarInheritedFromParent_AppliedToChild()
    {
        var html = @"<html><head><style>
            .parent { --text-color: navy; }
            .child { color: var(--text-color); }
        </style></head><body>
            <div class='parent'><p class='child'>Inherited var text</p></div>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Inherited var text");
    }

    [Fact]
    public async Task CalcWithPercentage_ResolvesCorrectly()
    {
        var html = @"<div style='width: calc(50% - 10px); background-color: red; height: 30px'>Half minus 10</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Half minus 10");
        text.Should().Contain("%PDF-1.7", "should produce valid PDF");
    }

    [Fact]
    public async Task MinMaxFunction_InWidth()
    {
        var html = @"<div style='width: min(200px, 50%); background-color: green; height: 30px'>Min width</div>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Min width");
    }
}
