using System;
using System.Text;
using System.Text.RegularExpressions;
using EggPdf.Fluent;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Fluent;

/// <summary>Building a document step by step (Document.New()/AddPage()/Header()/Content()...) instead of one nested callback.</summary>
public class ImperativeBuilderTests
{
    private static string Normalize(byte[] pdf)
        => Regex.Replace(Encoding.Latin1.GetString(pdf), @"/CreationDate \(D:\d+Z\)", "/CreationDate (X)");

    private static float TextY(string content, string word)
    {
        var m = Regex.Match(content, @"(-?\d+\.\d+) (-?\d+\.\d+) Td \(" + Regex.Escape(word) + @"\) Tj");
        m.Success.Should().BeTrue($"a positioned text run for '{word}' must exist");
        return float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void StepByStep_ProducesTheSameBytesAsTheNestedCallbackForm()
    {
        byte[] nested = Document.Create(doc => doc
                .Title("Report")
                .Page(page => page
                    .Size(PageSize.Letter).Margin(40)
                    .Header(h => h.Text("Acme").Bold())
                    .Footer(f => f.AlignCenter().PageNumberOfTotal())
                    .Content(c =>
                    {
                        c.Item().Text("Hello").FontSize(20);
                        c.Item().Row(r => { r.RelativeItem().Text("L"); r.RelativeItem().Text("R"); });
                    })))
            .Render();

        var doc2 = Document.New().Title("Report");
        var page2 = doc2.AddPage().Size(PageSize.Letter).Margin(40);
        page2.Header().Text("Acme").Bold();
        page2.Footer().AlignCenter().PageNumberOfTotal();
        var body = page2.Content();
        body.Item().Text("Hello").FontSize(20);
        var row = body.Item().Row();
        row.RelativeItem().Text("L");
        row.RelativeItem().Text("R");
        byte[] stepwise = doc2.Render();

        Normalize(stepwise).Should().Be(Normalize(nested));
    }

    [Fact]
    public void PageSettings_MayBeChangedAfterContentWasAdded()
    {
        var doc = Document.New();
        var page = doc.AddPage();
        page.Content().Item().Text("Early content");
        page.Size(PageSize.Letter); // set late: the @page rule is only written when the document is built

        Encoding.Latin1.GetString(doc.Render()).Should().Contain("/MediaBox [0 0 612.00 792.00]");
    }

    [Fact]
    public void ContentColumn_CanBeUsedRepeatedly_ItemsAppendInOrder()
    {
        var doc = Document.New();
        var page = doc.AddPage();
        page.Content().Item().Text("First");
        page.Content().Item().Text("Second");

        var text = Encoding.Latin1.GetString(doc.Render());
        TextY(text, "First").Should().BeGreaterThan(TextY(text, "Second"));
    }

    [Fact]
    public void HelperMethods_ComposeSectionsAcrossMethods()
    {
        var doc = Document.New().Title("Composed");
        var page = doc.AddPage().Margin(30);
        AddBranding(page);
        AddSummaryTable(page.Content());

        var text = Encoding.Latin1.GetString(doc.Render());
        text.Should().Contain("Brand");
        text.Should().Contain("Widget");

        static void AddBranding(PageDescriptor p) => p.Header().Text("Brand").Bold();
        static void AddSummaryTable(ColumnDescriptor c)
        {
            var table = c.Item().Table();
            table.Header(h => h.Cell().Text("Item"));
            table.Row(r => r.Cell().Text("Widget"));
        }
    }

    [Fact]
    public void MultipleAddPage_GiveEachGroupItsOwnSize()
    {
        var doc = Document.New();
        doc.AddPage().Size(PageSize.A4).Content().Item().Text("Portrait");
        doc.AddPage().Size(PageSize.A4.Landscape()).Content().Item().Text("Landscape");

        var boxes = Regex.Matches(Encoding.Latin1.GetString(doc.Render()), @"/MediaBox \[0 0 ([\d.]+) ([\d.]+)\]");
        boxes.Count.Should().Be(2);
        float.Parse(boxes[1].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeGreaterThan(float.Parse(boxes[0].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Render_CanBeCalledRepeatedly_WithIdenticalOutput()
    {
        var doc = Document.New();
        var page = doc.AddPage();
        page.Footer().PageNumberOfTotal();
        page.Content().Item().Text("Body");

        Normalize(doc.Render()).Should().Be(Normalize(doc.Render()));
    }

    [Fact]
    public void AfterRender_AnyFurtherChangeThrows_InsteadOfBeingSilentlyLost()
    {
        var doc = Document.New();
        var page = doc.AddPage();
        var item = page.Content().Item().Text("Body");
        doc.Render();

        Action addText = () => item.Text("Too late");
        Action style = () => item.Bold();
        Action addPage = () => doc.AddPage();
        Action title = () => doc.Title("Late");
        Action encrypt = () => doc.Encrypt(new EggPdf.Pdf.PdfEncryption { OwnerPassword = "x" });
        Action header = () => page.Header();

        addText.Should().Throw<InvalidOperationException>();
        style.Should().Throw<InvalidOperationException>();
        addPage.Should().Throw<InvalidOperationException>();
        title.Should().Throw<InvalidOperationException>();
        encrypt.Should().Throw<InvalidOperationException>();
        header.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ImperativeGridAndLists_RenderTheirItems()
    {
        var doc = Document.New();
        var c = doc.AddPage().Content();
        var grid = c.Item().Grid(2);
        grid.Item().Text("Cell1");
        grid.Item().Text("Cell2");
        var list = c.Item().BulletList();
        list.Item().Text("Apple");
        c.Item().NumberedList().Item().Text("First");

        var text = Encoding.Latin1.GetString(doc.Render());
        foreach (var w in new[] { "Cell1", "Cell2", "Apple", "First" }) text.Should().Contain(w);
    }
}
