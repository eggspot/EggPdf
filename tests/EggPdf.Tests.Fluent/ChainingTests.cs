using System.Text;
using System.Text.RegularExpressions;
using EggPdf.Fluent;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Fluent;

/// <summary>Pure method-chaining form: every child-adding method can be followed by the next sibling on the same parent.</summary>
public class ChainingTests
{
    private static string Normalize(byte[] pdf)
        => Regex.Replace(Encoding.Latin1.GetString(pdf), @"/CreationDate \(D:\d+Z\)", "/CreationDate (X)");

    private static float TextX(string content, string word)
    {
        var m = Regex.Match(content, @"(-?\d+\.\d+) (-?\d+\.\d+) Td \(" + Regex.Escape(word) + @"\) Tj");
        m.Success.Should().BeTrue($"a positioned text run for '{word}' must exist");
        return float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Chained() => Document.Create(doc => doc
            .Title("Chained")
            .Page(p => p.Size(PageSize.A4).Margin(30).Content(c => c
                .Item(i => i.Text("Intro").Bold())
                .Heading(HeadingLevel.H2, "Section", h => h.FontColor(Colors.Navy))
                .Item(i => i.Row(r => r
                    .Gap(5)
                    .RelativeItem(x => x.Text("Left"))
                    .ConstantItem(60, x => x.Text("Mid"))
                    .RelativeItem(2, x => x.Text("Wide"))))
                .Item(i => i.Grid(2, g => g
                    .Item(x => x.Text("G1"))
                    .Item(x => x.Text("G2"))
                    .Item(2, x => x.Text("Span"))))
                .Item(i => i.BulletList(l => l
                    .Item(x => x.Text("B1"))
                    .Item(x => x.Text("B2"))))
                .Item(i => i.Table(t => t
                    .Header(h => h.Cell(x => x.Text("H1")).Cell(x => x.Text("H2")), r => r.Background(Colors.LightGray))
                    .Row(r => r.Cell(x => x.Text("D1")).Cell(x => x.Text("D2")), r => r.AvoidBreakInside()))))))
        .Render();

    private static byte[] Classic() => Document.Create(doc => doc
            .Title("Chained")
            .Page(p => p.Size(PageSize.A4).Margin(30).Content(c =>
            {
                c.Item().Text("Intro").Bold();
                c.Heading(HeadingLevel.H2, "Section").FontColor(Colors.Navy);
                c.Item().Row(r =>
                {
                    r.Gap(5);
                    r.RelativeItem().Text("Left");
                    r.ConstantItem(60).Text("Mid");
                    r.RelativeItem(2).Text("Wide");
                });
                c.Item().Grid(2, g =>
                {
                    g.Item().Text("G1");
                    g.Item().Text("G2");
                    g.Item(2).Text("Span");
                });
                c.Item().BulletList(l =>
                {
                    l.Item().Text("B1");
                    l.Item().Text("B2");
                });
                c.Item().Table(t =>
                {
                    t.Header(h => { h.Cell().Text("H1"); h.Cell().Text("H2"); }).Background(Colors.LightGray);
                    t.Row(r => { r.Cell().Text("D1"); r.Cell().Text("D2"); }).AvoidBreakInside();
                });
            })))
        .Render();

    [Fact]
    public void ChainedForm_ProducesTheSameBytesAsTheStatementForm()
    {
        Normalize(Chained()).Should().Be(Normalize(Classic()));
    }

    [Fact]
    public void ChainedForm_RendersEveryElementInOrder()
    {
        var text = Encoding.Latin1.GetString(Chained());
        foreach (var w in new[] { "Intro", "Section", "Left", "Mid", "Wide", "G1", "G2", "Span", "B1", "B2", "H1", "H2", "D1", "D2" })
            text.Should().Contain(w);
        TextX(text, "Mid").Should().BeGreaterThan(TextX(text, "Left"));
        TextX(text, "Wide").Should().BeGreaterThan(TextX(text, "Mid"));
        TextX(text, "G2").Should().BeGreaterThan(TextX(text, "G1"));
        TextX(text, "H2").Should().BeGreaterThan(TextX(text, "H1"));
    }

    [Fact]
    public void PageChain_AddsSeveralPageGroupsInOneExpression()
    {
        byte[] pdf = Document.Create(doc => doc
                .Page(p => p.Size(PageSize.A4).Content(c => c.Item(i => i.Text("Portrait"))))
                .Page(p => p.Size(PageSize.A4.Landscape()).Content(c => c.Item(i => i.Text("Landscape")))))
            .Render();

        Regex.Matches(Encoding.Latin1.GetString(pdf), @"/MediaBox").Count.Should().Be(2);
    }
}
