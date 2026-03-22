using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>
/// Tests that CSS from &lt;style&gt; tags is applied to the rendered PDF output.
/// This verifies the cascade resolver is wired into the main pipeline.
/// </summary>
public class StyleTagRenderTests
{
    [Fact]
    public async Task StyleTag_BoldClass_UsesBoldFont()
    {
        var html = @"<html><head><style>
            .bold { font-weight: bold; }
        </style></head><body>
            <p class='bold'>Bold from stylesheet</p>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Bold from stylesheet");
        text.Should().Contain("Helvetica-Bold", "class .bold should apply font-weight:bold -> Helvetica-Bold");
    }

    [Fact]
    public async Task StyleTag_ItalicClass_UsesItalicFont()
    {
        var html = @"<html><head><style>
            .italic { font-style: italic; }
        </style></head><body>
            <p class='italic'>Italic from stylesheet</p>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Italic from stylesheet");
        text.Should().Contain("Oblique", "class .italic should apply font-style:italic -> Helvetica-Oblique");
    }

    [Fact]
    public async Task StyleTag_MonospaceClass_UsesCourier()
    {
        var html = @"<html><head><style>
            .code { font-family: monospace; }
        </style></head><body>
            <p class='code'>Monospace text</p>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Monospace text");
        text.Should().Contain("Courier", "class .code with font-family:monospace should use Courier");
    }

    [Fact]
    public async Task StyleTag_IdSelector_Applied()
    {
        var html = @"<html><head><style>
            #title { font-weight: bold; }
        </style></head><body>
            <p id='title'>ID-selected bold</p>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("ID-selected bold");
        text.Should().Contain("Helvetica-Bold");
    }

    [Fact]
    public async Task StyleTag_TypeSelector_Applied()
    {
        var html = @"<html><head><style>
            em { font-family: monospace; }
        </style></head><body>
            <p><em>Custom em style</em></p>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Custom em style");
        text.Should().Contain("Courier");
    }

    [Fact]
    public async Task StyleTag_MediaPrint_Applied()
    {
        var html = @"<html><head><style>
            @media print { p { font-weight: bold; } }
        </style></head><body>
            <p>Print bold text</p>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Print bold text");
        text.Should().Contain("Helvetica-Bold", "@media print rules should be applied");
    }

    [Fact]
    public async Task StyleTag_MediaScreen_Ignored()
    {
        var html = @"<html><head><style>
            @media screen { p { font-family: monospace; } }
        </style></head><body>
            <p>Not monospace in print</p>
        </body></html>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Not monospace in print");
        text.Should().NotContain("Courier", "@media screen rules should be ignored in print mode");
    }

    [Fact]
    public async Task TextColor_Red_ProducesColorOperator()
    {
        var html = "<p style='color: red'>Red text</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Red text");
        // Should contain "1.00 0.00 0.00 rg" or similar red fill color
        text.Should().Contain("1.00 0.00 0.00 rg", "red color should produce 1 0 0 rg operator");
    }

    [Fact]
    public async Task TextColor_NamedBlue_ProducesColorOperator()
    {
        var html = "<p style='color: blue'>Blue text</p>";

        byte[] pdf = await HtmlToPdf.RenderAsync(html);
        var text = Encoding.ASCII.GetString(pdf);

        text.Should().Contain("Blue text");
        text.Should().Contain("0.00 0.00 1.00 rg", "blue color should produce 0 0 1 rg operator");
    }
}
