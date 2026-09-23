using System;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class PdfUa2E2ETests
{
    [Fact]
    public void Render_TaggedWithUa2_UsesPdf20HeaderAndNamespace()
    {
        var pdf = HtmlToPdf.Render("<h1>Report</h1><p>Hello World</p>", tagged: true, uaVersion: PdfUaVersion.Ua2);
        var text = PdfAssert.ValidPdf(pdf, "Report", "Hello World");

        text.Should().StartWith("%PDF-2.0");
        text.Should().Contain("/Type /Namespace /NS (http://iso.org/pdf2/ssn)");
        text.Should().Contain("<pdfuaid:part>2</pdfuaid:part>");
        text.Should().Contain("<pdfuaid:rev>2024</pdfuaid:rev>");
    }

    [Fact]
    public void Render_TaggedWithoutUaVersion_DefaultsToUa1Pdf17()
    {
        var pdf = HtmlToPdf.Render("<h1>Report</h1>", tagged: true);
        var text = PdfAssert.ValidPdf(pdf, "Report");

        text.Should().StartWith("%PDF-1.7");
        text.Should().Contain("<pdfuaid:part>1</pdfuaid:part>");
    }

    [Fact]
    public void Render_Ua2LandmarkElements_ResolveToStandardFallbackTypesNotCustomNames()
    {
        var html = "<html><body><nav>Menu</nav><header>Head</header>" +
                    "<main><article>Art</article><section>Sec</section></main>" +
                    "<aside>Side</aside><footer>Foot</footer></body></html>";

        var pdf = HtmlToPdf.Render(html, tagged: true, uaVersion: PdfUaVersion.Ua2);
        var text = PdfAssert.ValidPdf(pdf, "Menu", "Head", "Art", "Sec", "Side", "Foot");

        // No custom landmark type names under UA-2 -- resolved straight to Div/Sect.
        text.Should().NotContain("/S /Nav");
        text.Should().NotContain("/S /Header");
        text.Should().NotContain("/S /Footer");
        text.Should().NotContain("/S /Aside");
        text.Should().NotContain("/S /Main");
        text.Should().NotContain("/S /Article");
        text.Should().NotContain("/S /Section");
        text.Should().NotContain("/RoleMap");

        text.Should().Contain("/S /Div");
        text.Should().Contain("/S /Sect");
    }

    [Fact]
    public void Render_Ua2CombinedWithPdfAConformance_Throws()
    {
        Action act = () => HtmlToPdf.Render("<h1>Hi</h1>", PdfAConformance.PdfA2b, tagged: true, uaVersion: PdfUaVersion.Ua2);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Render_PdfRenderOptionsUaVersion_Ua2_ProducesPdf20Header()
    {
        var pdf = HtmlToPdf.Render("<h1>Hi</h1>", new PdfRenderOptions { Tagged = true, UaVersion = PdfUaVersion.Ua2 });
        var text = PdfAssert.ValidPdf(pdf, "Hi");
        text.Should().StartWith("%PDF-2.0");
    }
}
