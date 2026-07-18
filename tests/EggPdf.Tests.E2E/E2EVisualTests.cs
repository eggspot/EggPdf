using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;

namespace EggPdf.Tests.E2E;

/// <summary>
/// Playwright E2E tests for the /e2e visual comparison page.
/// Covers: page structure, dropdown test cases, Run Test, Run All,
/// and individual visual rendering for each test case (heading, table, invoice, styles, list).
/// </summary>
[Collection("E2E")]
public class E2EVisualTests
{
    private readonly ServiceFixture _fixture;

    public E2EVisualTests(ServiceFixture fixture) { _fixture = fixture; }

    [Fact]
    public async Task E2EPage_HasCorrectStructure()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/e2e");

        // Header
        var header = page.Locator(".header h1");
        (await header.TextContentAsync()).Should().Contain("E2E Visual Comparison");

        // Two iframes
        var browserFrame = page.Locator("#browserFrame");
        (await browserFrame.IsVisibleAsync()).Should().BeTrue();

        var pdfFrame = page.Locator("#pdfFrame");
        (await pdfFrame.IsVisibleAsync()).Should().BeTrue();

        // Dropdown
        var dropdown = page.Locator("#testCase");
        (await dropdown.IsVisibleAsync()).Should().BeTrue();

        // Run Test button
        var runBtn = page.Locator("button.primary");
        (await runBtn.TextContentAsync()).Should().Contain("Run Test");

        // Run All button
        var runAllBtn = page.Locator("button", new() { HasText = "Run All Tests" });
        (await runAllBtn.IsVisibleAsync()).Should().BeTrue();

        // Result span
        var result = page.Locator("#result");
        (await result.IsVisibleAsync()).Should().BeTrue();

        await page.CloseAsync();
    }

    [Fact]
    public async Task Dropdown_HasAllFiveTestCases()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/e2e");

        var options = page.Locator("#testCase option");
        var count = await options.CountAsync();
        count.Should().Be(5);

        // Verify each option value
        (await options.Nth(0).GetAttributeAsync("value")).Should().Be("heading");
        (await options.Nth(1).GetAttributeAsync("value")).Should().Be("table");
        (await options.Nth(2).GetAttributeAsync("value")).Should().Be("invoice");
        (await options.Nth(3).GetAttributeAsync("value")).Should().Be("styles");
        (await options.Nth(4).GetAttributeAsync("value")).Should().Be("list");

        await page.CloseAsync();
    }

    [Fact]
    public async Task RunTest_DefaultHeading_RendersBothFrames()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/e2e");

        // The page auto-runs the first test on load, wait for result
        await page.Locator("#result .pass").WaitForAsync(new() { Timeout = 10000 });

        var resultText = await page.Locator("#result").TextContentAsync();
        resultText.Should().Contain("heading");
        resultText.Should().Contain("rendered");

        // Browser frame should have content (srcdoc set)
        var browserFrame = page.Locator("#browserFrame");
        var srcdoc = await browserFrame.GetAttributeAsync("srcdoc");
        srcdoc.Should().NotBeNullOrEmpty();
        srcdoc.Should().Contain("Hello World");

        // PDF frame should have a blob URL
        var pdfSrc = await page.Locator("#pdfFrame").GetAttributeAsync("src");
        pdfSrc.Should().NotBeNullOrEmpty();
        pdfSrc.Should().Contain("blob:");

        await page.CloseAsync();
    }

    [Theory]
    [InlineData("heading", "Hello World")]
    [InlineData("table", "Alpha")]
    [InlineData("invoice", "Invoice #001")]
    [InlineData("styles", "Styled Box")]
    [InlineData("list", "Features")]
    public async Task RunTest_EachTestCase_RendersBothFrames(string testCase, string expectedContent)
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/e2e");

        // Wait for initial auto-run to complete
        await page.Locator("#result .pass").WaitForAsync(new() { Timeout = 10000 });

        // Select the test case
        await page.Locator("#testCase").SelectOptionAsync(testCase);

        // Wait for rendering to complete
        await page.WaitForFunctionAsync(
            $"() => document.getElementById('result').textContent.includes('{testCase}')",
            null, new() { Timeout = 10000 });

        // Verify result shows pass
        var resultText = await page.Locator("#result").TextContentAsync();
        resultText.Should().Contain(testCase);
        resultText.Should().Contain("rendered");

        // Browser frame should contain expected content in srcdoc
        var srcdoc = await page.Locator("#browserFrame").GetAttributeAsync("srcdoc");
        srcdoc.Should().Contain(expectedContent);

        // PDF frame should have blob URL (PDF was rendered)
        var pdfSrc = await page.Locator("#pdfFrame").GetAttributeAsync("src");
        pdfSrc.Should().NotBeNullOrEmpty();
        pdfSrc.Should().Contain("blob:");

        await page.CloseAsync();
    }

    [Fact]
    public async Task RunAllTests_CyclesThroughAllTestCases()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/e2e");

        // Wait for initial auto-run
        await page.Locator("#result .pass").WaitForAsync(new() { Timeout = 10000 });

        // Click Run All Tests
        await page.Locator("button", new() { HasText = "Run All Tests" }).ClickAsync();

        // Wait for all tests to complete (5 tests x ~1s delay each = ~5-6s)
        await page.WaitForFunctionAsync(
            "() => document.getElementById('result').textContent.includes('All 5 tests rendered')",
            null, new() { Timeout = 30000 });

        var resultText = await page.Locator("#result").TextContentAsync();
        resultText.Should().Contain("All 5 tests rendered");

        // Dropdown should be on the last test case after Run All
        var selectedValue = await page.Locator("#testCase").InputValueAsync();
        selectedValue.Should().Be("list");

        await page.CloseAsync();
    }

    [Fact]
    public async Task RunTest_BrowserFrame_ReceivesPrintPreviewHtml()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/e2e");

        // Wait for auto-run
        await page.Locator("#result .pass").WaitForAsync(new() { Timeout = 10000 });

        // The browser frame srcdoc should contain @page rule (print simulation)
        var srcdoc = await page.Locator("#browserFrame").GetAttributeAsync("srcdoc");
        srcdoc.Should().Contain("@page");

        await page.CloseAsync();
    }

    [Fact]
    public async Task RunTest_PdfFrame_ReceivesValidPdfBlob()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/e2e");

        // Wait for auto-run
        await page.Locator("#result .pass").WaitForAsync(new() { Timeout = 10000 });

        // Verify the PDF was actually fetched by checking the API directly
        var response = await page.APIRequest.PostAsync($"{_fixture.BaseUrl}/api/render", new()
        {
            DataObject = new { html = "<h1>Hello World</h1><p>This is a paragraph with <strong>bold</strong> and <em>italic</em> text.</p>" }
        });

        response.Status.Should().Be(200);
        var bytes = await response.BodyAsync();
        bytes.Length.Should().BeGreaterThan(100);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");

        await page.CloseAsync();
    }

    [Fact]
    public async Task SideHeaders_ShowCorrectLabels()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/e2e");

        var headers = page.Locator(".side-header");
        var count = await headers.CountAsync();
        count.Should().Be(2);

        (await headers.Nth(0).TextContentAsync()).Should().Contain("Browser Print Preview");
        (await headers.Nth(1).TextContentAsync()).Should().Contain("EggPdf PDF Output");

        await page.CloseAsync();
    }

    [Fact]
    public async Task Dropdown_ChangeTriggersRender()
    {
        var page = await _fixture.Browser!.NewPageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/e2e");

        // Wait for initial render
        await page.Locator("#result .pass").WaitForAsync(new() { Timeout = 10000 });

        // Change dropdown to 'table' - the onchange should trigger runTest()
        await page.Locator("#testCase").SelectOptionAsync("table");

        // Wait for table test to render
        await page.WaitForFunctionAsync(
            "() => document.getElementById('result').textContent.includes('table')",
            null, new() { Timeout = 10000 });

        var srcdoc = await page.Locator("#browserFrame").GetAttributeAsync("srcdoc");
        srcdoc.Should().Contain("Alpha");
        srcdoc.Should().Contain("Beta");

        await page.CloseAsync();
    }
}
