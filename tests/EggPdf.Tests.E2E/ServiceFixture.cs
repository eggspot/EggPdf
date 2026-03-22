using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace EggPdf.Tests.E2E;

/// <summary>
/// Shared fixture that starts the EggPdf.Service and Playwright browser
/// for all E2E tests. Service runs on a random port to avoid conflicts.
/// </summary>
public class ServiceFixture : IAsyncLifetime
{
    private Process? _serviceProcess;
    public int Port { get; private set; }
    public string BaseUrl => $"http://localhost:{Port}";
    public IPlaywright? Playwright { get; private set; }
    public IBrowser? Browser { get; private set; }

    public async Task InitializeAsync()
    {
        // Pick a random port
        Port = new Random().Next(15000, 16000);

        // Start the service
        var projectDir = FindProjectDir();
        _serviceProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{projectDir}\" -c Release -- --urls http://localhost:{Port}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        _serviceProcess.Start();

        // Wait for service to be ready
        using var client = new HttpClient();
        for (int i = 0; i < 30; i++)
        {
            try
            {
                var resp = await client.GetAsync($"{BaseUrl}/health");
                if (resp.IsSuccessStatusCode) break;
            }
            catch { }
            await Task.Delay(1000);
        }

        // Start Playwright
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async Task DisposeAsync()
    {
        if (Browser != null) await Browser.CloseAsync();
        Playwright?.Dispose();

        if (_serviceProcess != null && !_serviceProcess.HasExited)
        {
            _serviceProcess.Kill(true);
            _serviceProcess.Dispose();
        }
    }

    private static string FindProjectDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            var candidate = System.IO.Path.Combine(dir, "src", "EggPdf.Service", "EggPdf.Service.csproj");
            if (System.IO.File.Exists(candidate))
                return System.IO.Path.GetDirectoryName(candidate)!;
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        // Fallback: relative from test project
        return System.IO.Path.GetFullPath(
            System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "EggPdf.Service"));
    }
}

[CollectionDefinition("E2E")]
public class E2ECollection : ICollectionFixture<ServiceFixture> { }
