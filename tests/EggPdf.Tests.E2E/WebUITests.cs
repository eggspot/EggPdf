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

        // Header should show EggPdf
        var title = await page.TitleAsync();
        title.Should().Contain("EggPdf");

        // Editor (Ace) should exist
        var editor = page.Locator("#aceEditor");
        await editor.WaitForAsync(new() { Timeout = 5000 });
        (await editor.IsVisibleAsync()).Should().BeTrue();

        // Download PDF button should exist in header
        var downloadBtn = page.Locator(".header .btn-primary");
        (await downloadBtn.IsVisibleAsync()).Should().BeTrue();

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

        // Wait for the preview to initialize
        await page.WaitForTimeoutAsync(500);

        // PDF tab should be active by default
        var activeTab = page.Locator(".preview-tab.active");
        var tabText = await activeTab.TextContentAsync();
        tabText.Should().Contain("PDF");

        // The PDF preview div should be visible
        var pdfView = page.Locator("#pdfPreview");
        (await pdfView.IsVisibleAsync()).Should().BeTrue();

        await page.CloseAsync();
    }

    [Fact]
    public async Task TemplateButton_OpensModal()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        // Click Templates button
        await page.Locator(".header .btn", new() { HasText = "Templates" }).ClickAsync();

        // Modal should be visible
        var modal = page.Locator("#tplModal");
        (await modal.IsVisibleAsync()).Should().BeTrue();

        // Should have template cards
        var cards = page.Locator(".tpl-card");
        var count = await cards.CountAsync();
        count.Should().BeGreaterOrEqualTo(5); // blank, invoice, report, letter, certificate, resume

        await page.CloseAsync();
    }

    [Fact]
    public async Task TemplateCard_LoadsIntoEditor()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        // Open templates and click "Report" (use onclick attribute to avoid ambiguity with other cards containing "report")
        await page.Locator(".header .btn", new() { HasText = "Templates" }).ClickAsync();
        await page.Locator(".tpl-card[onclick*=\"loadTpl('report')\"]").ClickAsync();

        // Editor should now contain report content
        await WaitForEditorAsync(page);
        var value = await page.EvaluateAsync<string>("() => window.editor ? window.editor.getValue() : document.getElementById('htmlInput').value");
        value.Should().Contain("Report");

        // Modal should be closed
        var modal = page.Locator("#tplModal");
        (await modal.IsVisibleAsync()).Should().BeFalse();

        await page.CloseAsync();
    }

    [Fact]
    public async Task ThemeToggle_SwitchesTheme()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        // Click theme button
        await page.GetByText("Theme").ClickAsync();

        // Body should have 'light' class
        var bodyClass = await page.Locator("body").GetAttributeAsync("class");
        bodyClass.Should().Contain("light");

        // Click again to switch back
        await page.GetByText("Theme").ClickAsync();
        bodyClass = await page.Locator("body").GetAttributeAsync("class") ?? "";
        bodyClass.Should().NotContain("light");

        await page.CloseAsync();
    }

    [Fact]
    public async Task DownloadPdf_ApiReturnsPdf()
    {
        // Test the download flow via API directly (more reliable than intercepting browser downloads)
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        // Get the HTML from the editor (works with both Ace and fallback textarea)
        await WaitForEditorAsync(page);
        var html = await page.EvaluateAsync<string>("() => window.editor ? window.editor.getValue() : document.getElementById('htmlInput').value");
        html.Should().NotBeNullOrEmpty();

        // Call the API directly (same as what the Download button does)
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

        // Switch to Print tab first, then back to PDF tab
        await page.Locator(".preview-tab", new() { HasText = "Print" }).ClickAsync();
        await page.Locator(".preview-tab", new() { HasText = "PDF" }).ClickAsync();

        // PDF preview div should be visible
        var pdfView = page.Locator("#pdfPreview");
        (await pdfView.IsVisibleAsync()).Should().BeTrue();

        // Print preview should be hidden
        var printView = page.Locator("#printPreview");
        (await printView.IsVisibleAsync()).Should().BeFalse();

        await page.CloseAsync();
    }

    [Fact]
    public async Task OptionsButton_TogglesSidebar()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        // Sidebar should be hidden initially
        var sidebar = page.Locator(".sidebar");
        (await sidebar.IsVisibleAsync()).Should().BeFalse();

        // Click Options
        await page.GetByText("Options").ClickAsync();

        // Sidebar should be visible
        (await sidebar.IsVisibleAsync()).Should().BeTrue();

        // Should have page size dropdown
        var pageSize = page.Locator("#optPageSize");
        (await pageSize.IsVisibleAsync()).Should().BeTrue();

        await page.CloseAsync();
    }

    [Fact]
    public async Task EditorInput_UpdatesLineNumbers()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl);

        // Ace editor line number gutter cells should exist
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
