using System;
using System.IO;
using System.Threading.Tasks;
using EggPdf.Core.Resources;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Core;

public class FileResourceResolverTests
{
    [Fact]
    public async Task Resolve_ExistingFile_ReturnsBytes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "eggpdf_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.txt");
        File.WriteAllText(filePath, "hello world");

        try
        {
            var resolver = new FileResourceResolver(tempDir);
            var result = await resolver.ResolveAsync("test.txt", ResourceType.Other);

            result.Should().NotBeNull();
            System.Text.Encoding.UTF8.GetString(result!.Data).Should().Be("hello world");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Resolve_NonExistentFile_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "eggpdf_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var resolver = new FileResourceResolver(tempDir);
            var result = await resolver.ResolveAsync("nonexistent.txt", ResourceType.Other);

            result.Should().BeNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Resolve_PathTraversal_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "eggpdf_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var resolver = new FileResourceResolver(tempDir);

            // Attempt path traversal - should be rejected
            var result = await resolver.ResolveAsync("../../etc/passwd", ResourceType.Other);

            result.Should().BeNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Resolve_AbsolutePathOutsideBase_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "eggpdf_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var resolver = new FileResourceResolver(tempDir);
            var result = await resolver.ResolveAsync("/etc/passwd", ResourceType.Other);

            result.Should().BeNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Resolve_HttpUrl_ReturnsNull()
    {
        var resolver = new FileResourceResolver(Path.GetTempPath());
        var result = await resolver.ResolveAsync("https://example.com/file.txt", ResourceType.Other);

        result.Should().BeNull();
    }
}
