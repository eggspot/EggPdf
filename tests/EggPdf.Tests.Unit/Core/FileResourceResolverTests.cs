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

    [Fact]
    public async Task Resolve_FileLargerThanMaxFileSize_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "eggpdf_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        File.WriteAllBytes(Path.Combine(tempDir, "big.bin"), new byte[2000]);

        try
        {
            var resolver = new FileResourceResolver(tempDir) { MaxFileSizeBytes = 1000 };
            var result = await resolver.ResolveAsync("big.bin", ResourceType.Image);

            result.Should().BeNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Resolve_SymlinkPointingOutsideBase_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "eggpdf_test_" + Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "eggpdf_outside_" + Guid.NewGuid().ToString("N") + ".txt");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(outside, "secret");

        try
        {
            try { File.CreateSymbolicLink(Path.Combine(tempDir, "link.txt"), outside); }
            catch (Exception) { return; } // Windows without symlink privilege: nothing to test
            try { Directory.CreateSymbolicLink(Path.Combine(tempDir, "dirlink"), Path.GetDirectoryName(outside)!); }
            catch (Exception) { return; }
            var resolver = new FileResourceResolver(tempDir);

            (await resolver.ResolveAsync("link.txt", ResourceType.Other)).Should().BeNull("a file symlink must not escape the base directory");
            (await resolver.ResolveAsync("dirlink/" + Path.GetFileName(outside), ResourceType.Other))
                .Should().BeNull("a directory symlink must not escape the base directory");
        }
        finally
        {
            Directory.Delete(tempDir, true);
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task Resolve_SymlinkInsideBase_ReturnsBytes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "eggpdf_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "real.txt"), "inside");

        try
        {
            try { File.CreateSymbolicLink(Path.Combine(tempDir, "link.txt"), Path.Combine(tempDir, "real.txt")); }
            catch (Exception) { return; }
            var resolver = new FileResourceResolver(tempDir);

            var result = await resolver.ResolveAsync("link.txt", ResourceType.Other);

            System.Text.Encoding.UTF8.GetString(result!.Data).Should().Be("inside");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
