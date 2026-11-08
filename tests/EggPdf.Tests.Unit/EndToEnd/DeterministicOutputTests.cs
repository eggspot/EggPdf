using System;
using System.Diagnostics;
using System.Text;
using EggPdf.Core;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.EndToEnd;

public class DeterministicOutputTests
{
    [Fact]
    public void StableHash_IsTheSameEveryTimeAndDiffersForDifferentInput()
    {
        StableHash.Hex("data:image/png;base64,AAAA").Should().Be(StableHash.Hex("data:image/png;base64,AAAA"));
        StableHash.Hex("a").Should().NotBe(StableHash.Hex("b"));
        StableHash.Hex("").Should().Be("CBF29CE484222325", "the empty input hashes to the FNV-1a 64-bit offset basis");
        StableHash.Hex(new byte[] { 0x61 }).Should().Be("AF63DC4C8601EC8C", "FNV-1a 64 of 'a'");
    }

    [Fact]
    public void FilterOpacity_FadesDescendants()
    {
        var text = PdfAssert.ValidPdf(HtmlToPdf.Render("<div style='filter:opacity(50%)'><p>Faded</p></div>"));

        text.Should().Contain("/GS50 gs");
    }

    [Fact]
    public void ManyPages_AssigningBoxesStaysLinear()
    {
        var html = new StringBuilder("<div style='background:#eeeeee'>");
        for (int i = 0; i < 4000; i++) html.Append("<p style='margin:0;background:#fafafa'>Line</p>");
        html.Append("</div>");
        var source = html.ToString();
        HtmlToPdf.Render(source);

        var stopwatch = Stopwatch.StartNew();
        var pdf = HtmlToPdf.Render(source);
        stopwatch.Stop();

        PdfAssert.PageCount(PdfAssert.ValidPdf(pdf)).Should().BeGreaterThan(60);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20), "page assignment must not rescan every page per box");
    }
}
