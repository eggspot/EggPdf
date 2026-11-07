using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class MediaQueryRenderTests
{
    private static async Task<string> RenderAscii(string html) =>
        Encoding.ASCII.GetString(await HtmlToPdf.RenderAsync(html));

    [Fact]
    public async Task MobileBreakpointRule_NotAppliedToA4Page()
    {
        var content = await RenderAscii(
            "<html><head><style>.m{display:block} @media only screen and (max-width:600px){.m{display:none}} " +
            "@media (max-width:300px){.m{display:none}}</style></head>" +
            "<body><p class='m'>Visible</p></body></html>");

        content.Should().Contain("(Visible) Tj");
    }

    [Fact]
    public async Task PrintWidthRule_AppliedWhenPageMatches()
    {
        var content = await RenderAscii(
            "<html><head><style>.p{display:none} @media print and (min-width:500px){.p{display:block}}</style></head>" +
            "<body><p class='p'>Shown</p></body></html>");

        content.Should().Contain("(Shown) Tj");
    }

    [Fact]
    public async Task StyleElementWithScreenMedia_IsIgnored()
    {
        var content = await RenderAscii(
            "<html><head><style media='screen'>.s{display:none}</style></head>" +
            "<body><p class='s'>Printed</p></body></html>");

        content.Should().Contain("(Printed) Tj");
    }

    [Fact]
    public async Task StyleElementWithPrintMediaList_IsApplied()
    {
        var content = await RenderAscii(
            "<html><head><style media='screen, print'>.s{display:none}</style></head>" +
            "<body><p class='s'>Hidden</p><p>Other</p></body></html>");

        content.Should().NotContain("(Hidden) Tj");
    }

    [Fact]
    public async Task LandscapeRule_AppliedOnLandscapePage()
    {
        var content = await RenderAscii(
            "<html><head><style>@page{size:A4 landscape} .l{display:none} @media (orientation:landscape){.l{display:block}}</style></head>" +
            "<body><p class='l'>Landscape</p></body></html>");

        content.Should().Contain("(Landscape) Tj");
    }
}
