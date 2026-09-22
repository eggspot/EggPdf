using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

/// <summary>CSS Paged Media named pages: <c>page: name</c> on a block + <c>@page name { ... }</c>.</summary>
public class NamedPageTests
{
    private static List<(float w, float h)> MediaBoxes(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var boxes = new List<(float, float)>();
        foreach (Match m in Regex.Matches(text, @"/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)\s*\]"))
            boxes.Add((float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                       float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)));
        return boxes;
    }

    [Fact]
    public async Task NamedPage_UsesItsOwnPageSize()
    {
        var html = @"<html><head><style>
            @page { size: 600px 800px; margin: 20px }
            @page wide { size: 1000px 500px; margin: 10px }
            </style></head><body>
            <div>Portrait content</div>
            <div style='page: wide'>Wide content</div>
            </body></html>";

        var boxes = MediaBoxes(await HtmlToPdf.RenderAsync(html));

        boxes.Should().HaveCount(2, "the name change forces a page break, giving one page per group");
        boxes[0].w.Should().BeApproximately(450f, 1f);   // 600px = 450pt
        boxes[0].h.Should().BeApproximately(600f, 1f);
        boxes[1].w.Should().BeApproximately(750f, 1f);   // 1000px = 750pt
        boxes[1].h.Should().BeApproximately(375f, 1f);
    }

    [Fact]
    public async Task NamedPage_ReturningToUnnamed_RestoresBasePageSize()
    {
        var html = @"<html><head><style>
            @page { size: 600px 800px }
            @page landscape { size: 800px 600px }
            </style></head><body>
            <div>A</div>
            <div style='page: landscape'>B</div>
            <div>C</div>
            </body></html>";

        var boxes = MediaBoxes(await HtmlToPdf.RenderAsync(html));

        boxes.Should().HaveCount(3);
        boxes[0].w.Should().BeLessThan(boxes[0].h, "first group is portrait");
        boxes[1].w.Should().BeGreaterThan(boxes[1].h, "the named group is landscape");
        boxes[2].w.Should().BeLessThan(boxes[2].h, "unnamed content after the named block returns to the base page");
    }

    [Fact]
    public async Task ConsecutiveBlocksWithSameName_ShareOnePage()
    {
        var html = @"<html><head><style>
            @page wide { size: 1000px 500px }
            </style></head><body>
            <div style='page: wide'>One</div>
            <div style='page: wide'>Two</div>
            </body></html>";

        var boxes = MediaBoxes(await HtmlToPdf.RenderAsync(html));
        boxes.Should().HaveCount(1);
        boxes[0].w.Should().BeApproximately(750f, 1f);
    }

    [Fact]
    public async Task NamedPage_ContentReflowsToItsPageWidth()
    {
        // A wide page must not keep the base page's narrow content width: text that wraps on a
        // narrow page fits one line on the wide one, so the wide group needs no extra pages.
        var longLine = new string('x', 10).Replace("x", "word ") ;
        var html = "<html><head><style>@page { size: 300px 400px; margin: 0 } @page wide { size: 1200px 400px; margin: 0 }" +
                   "body{margin:0}</style></head><body><div style='page: wide'>" + longLine + longLine + "</div></body></html>";

        var boxes = MediaBoxes(await HtmlToPdf.RenderAsync(html));
        boxes.Should().HaveCount(1);
        boxes[0].w.Should().BeApproximately(900f, 1f); // 1200px
    }

    [Fact]
    public async Task NamedPageRuleWithoutAnyUse_ChangesNothing()
    {
        var html = @"<html><head><style>
            @page { size: 600px 800px }
            @page unused { size: 100px 100px }
            </style></head><body><div>Just one page</div></body></html>";

        var boxes = MediaBoxes(await HtmlToPdf.RenderAsync(html));
        boxes.Should().HaveCount(1);
        boxes[0].w.Should().BeApproximately(450f, 1f);
    }

    [Fact]
    public async Task PageProperty_WithoutNamedRule_DoesNotSplitDocument()
    {
        // No @page <name> rule exists, so `page:` has nothing to select and the ordinary path runs.
        var html = "<html><body><div style='page: chapter'>One</div><div>Two</div></body></html>";
        var boxes = MediaBoxes(await HtmlToPdf.RenderAsync(html));
        boxes.Should().HaveCount(1);
    }

    [Fact]
    public async Task NamedPage_OnANestedSection_SplitsTheWrapperAcrossPages()
    {
        var html = @"<html><head><style>
            @page { size: 600px 800px; margin: 20px }
            @page wide { size: 1000px 500px; margin: 10px }
            </style></head><body>
            <main>
              <section>Intro</section>
              <section style='page: wide'>Chart</section>
              <section>Outro</section>
            </main>
            </body></html>";

        var boxes = MediaBoxes(await HtmlToPdf.RenderAsync(html));

        boxes.Should().HaveCount(3, "the wrapper is fragmented around the named section");
        boxes[0].w.Should().BeApproximately(450f, 1f);
        boxes[1].w.Should().BeApproximately(750f, 1f);
        boxes[2].w.Should().BeApproximately(450f, 1f);
    }

    [Fact]
    public async Task NamedPage_TwoLevelsDeep_StillSplits()
    {
        var html = @"<html><head><style>
            @page wide { size: 1000px 500px }
            </style></head><body>
            <div><article><p>One</p><p style='page: wide'>Two</p></article></div>
            </body></html>";

        var boxes = MediaBoxes(await HtmlToPdf.RenderAsync(html));
        boxes.Should().HaveCount(2);
        boxes[1].w.Should().BeApproximately(750f, 1f);
    }

    [Fact]
    public async Task NestedNamedPage_KeepsStylingOfTheFragmentedWrapper()
    {
        // The two wrapper fragments must both keep the wrapper's own rules (here a background colour)
        var html = @"<html><head><style>
            @page wide { size: 1000px 500px }
            main { background-color: rgb(255, 0, 0); }
            </style></head><body>
            <main><p>One</p><p style='page: wide'>Two</p></main>
            </body></html>";

        var text = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(html));
        System.Text.RegularExpressions.Regex.Matches(text, @"1\.00 0\.00 0\.00 rg").Count
            .Should().BeGreaterOrEqualTo(2, "each page paints its fragment of <main> with the red background");
    }

    [Fact]
    public async Task PageNumbers_ContinueAcrossNamedPageGroups()
    {
        var html = @"<html><head><style>
            @page { size: 600px 800px; margin: 60px; @bottom-center { content: 'P' counter(page) '/' counter(pages) } }
            @page wide { size: 1000px 500px; margin: 60px }
            </style></head><body>
            <div>First</div>
            <div style='page: wide'>Second</div>
            </body></html>";

        var text = Encoding.Latin1.GetString(await HtmlToPdf.RenderAsync(html));
        text.Should().Contain("P1/2", "the first group's page is page 1 of 2");
        text.Should().Contain("P2/2", "numbering continues into the named group instead of restarting");
    }
}
