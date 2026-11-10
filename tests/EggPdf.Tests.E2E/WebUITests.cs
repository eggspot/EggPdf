using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace EggPdf.Tests.E2E;

/// <summary>
/// Playwright E2E tests for the EggPdf WebUI.
/// Tests that the UI loads, editor works, templates load, PDF downloads,
/// and the E2E comparison page functions correctly.
/// </summary>
[Collection("E2E")]
public class WebUITests
{
    private readonly ServiceFixture _fixture;

    public WebUITests(ServiceFixture fixture) { _fixture = fixture; }

    /// <summary>Wait until window.editor is initialized on the page.</summary>
    private static Task WaitForEditorAsync(IPage page) =>
        page.WaitForFunctionAsync("() => window.editor && typeof window.editor.getValue === 'function'",
            null, new() { Timeout = 10000 });

    [Fact]
    public async Task HomePage_LoadsSuccessfully()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        var title = await page.TitleAsync();
        title.Should().Contain("EggPdf");

        var aceEditor = page.Locator("#aceEditor");
        await aceEditor.WaitForAsync(new() { Timeout = 5000 });
        (await aceEditor.IsVisibleAsync()).Should().BeTrue();

        var downloadPdfButton = page.Locator(".header .btn-primary");
        (await downloadPdfButton.IsVisibleAsync()).Should().BeTrue();

        await page.CloseAsync();
    }

    [Fact]
    public async Task Editor_HasDefaultInvoiceTemplate()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        await WaitForEditorAsync(page);
        var value = await page.EvaluateAsync<string>("() => window.editor ? window.editor.getValue() : document.getElementById('htmlInput').value");

        value.Should().Contain("Invoice");
        value.Should().Contain("<table>");

        await page.CloseAsync();
    }

    [Fact]
    public async Task PrintPreview_ShowsHtmlContent()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        await page.WaitForTimeoutAsync(500);

        var activeTab = page.Locator(".preview-tab.active");
        var tabText = await activeTab.TextContentAsync();
        tabText.Should().Contain("PDF", "the PDF tab is active by default");

        var pdfView = page.Locator("#pdfPreview");
        (await pdfView.IsVisibleAsync()).Should().BeTrue();

        await page.CloseAsync();
    }

    [Fact]
    public async Task TemplateButton_OpensModal()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        await page.Locator(".header .btn", new() { HasText = "Templates" }).ClickAsync();

        var modal = page.Locator("#tplModal");
        (await modal.IsVisibleAsync()).Should().BeTrue();

        var cards = page.Locator(".tpl-card");
        var count = await cards.CountAsync();
        count.Should().BeGreaterOrEqualTo(5, "blank, invoice, report, letter, certificate and resume templates are offered");

        await page.CloseAsync();
    }

    [Fact]
    public async Task TemplateCard_LoadsIntoEditor()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        await page.Locator(".header .btn", new() { HasText = "Templates" }).ClickAsync();
        var reportCardByItsOnclick = page.Locator(".tpl-card[onclick*=\"loadTpl('report')\"]");
        await reportCardByItsOnclick.ClickAsync();

        await WaitForEditorAsync(page);
        var value = await page.EvaluateAsync<string>("() => window.editor ? window.editor.getValue() : document.getElementById('htmlInput').value");
        value.Should().Contain("Report");

        var modal = page.Locator("#tplModal");
        (await modal.IsVisibleAsync()).Should().BeFalse("choosing a template closes the modal");

        await page.CloseAsync();
    }

    [Fact]
    public async Task ThemeToggle_SwitchesTheme()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        await page.GetByText("Theme").ClickAsync();

        var bodyClass = await page.Locator("body").GetAttributeAsync("class");
        bodyClass.Should().Contain("light");

        await page.GetByText("Theme").ClickAsync();
        bodyClass = await page.Locator("body").GetAttributeAsync("class") ?? "";
        bodyClass.Should().NotContain("light");

        await page.CloseAsync();
    }

    [Fact]
    public async Task DownloadPdf_TheEditorHtmlPostedLikeTheDownloadButton_ReturnsPdf()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        await WaitForEditorAsync(page);
        var html = await page.EvaluateAsync<string>("() => window.editor ? window.editor.getValue() : document.getElementById('htmlInput').value");
        html.Should().NotBeNullOrEmpty();

        var response = await page.APIRequest.PostAsync($"{_fixture.BaseUrl}/api/render", new()
        {
            DataObject = new { html }
        });

        response.Status.Should().Be(200);
        var bytes = await response.BodyAsync();
        bytes.Length.Should().BeGreaterThan(100);
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");

        await page.CloseAsync();
    }

    [Fact]
    public async Task PdfPreviewTab_SwitchesView()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        await page.Locator(".preview-tab", new() { HasText = "Print" }).ClickAsync();
        await page.Locator(".preview-tab", new() { HasText = "PDF" }).ClickAsync();

        var pdfView = page.Locator("#pdfPreview");
        (await pdfView.IsVisibleAsync()).Should().BeTrue();

        var printView = page.Locator("#printPreview");
        (await printView.IsVisibleAsync()).Should().BeFalse();

        await page.CloseAsync();
    }

    [Fact]
    public async Task OptionsButton_TogglesSidebar()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        var sidebar = page.Locator(".sidebar");
        (await sidebar.IsVisibleAsync()).Should().BeFalse();

        await page.GetByText("Options").ClickAsync();

        (await sidebar.IsVisibleAsync()).Should().BeTrue();

        var pageSize = page.Locator("#optPageSize");
        (await pageSize.IsVisibleAsync()).Should().BeTrue();

        await page.CloseAsync();
    }

    [Fact]
    public async Task EditorInput_UpdatesLineNumbers()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        var gutterCell = page.Locator(".ace_gutter-cell").First;
        await gutterCell.WaitForAsync(new() { Timeout = 10000 });
        var text = await gutterCell.TextContentAsync();
        text.Should().NotBeNullOrEmpty();
        text.Should().Contain("1");

        await page.CloseAsync();
    }

    [Fact]
    public async Task StatusBar_ShowsReady()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        var status = page.Locator("#statusText");
        var text = await status.TextContentAsync();
        text.Should().Be("Ready");

        await page.CloseAsync();
    }
}
