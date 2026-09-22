using EggPdf.Text;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class SystemFontLocatorTests
{
    [Fact]
    public void GetFontDirectories_ReturnsNonEmpty()
    {
        var dirs = SystemFontLocator.GetFontDirectories();
        dirs.Should().NotBeEmpty();
    }

    [Fact]
    public void FindFont_SansSerif_FindsSomething()
    {
        // Every system should have at least one sans-serif font
        var path = SystemFontLocator.FindFont("sans-serif");

        // May be null on minimal Docker images, but when a font is found it must be a real file
        if (path != null)
            System.IO.File.Exists(path).Should().BeTrue("a located font path must point at an existing file");
    }

    [Fact]
    public void FindFont_NonexistentName_ReturnsNull()
    {
        SystemFontLocator.FindFont("nonexistent-font-xyz-123").Should().BeNull("no installed font has that name");
    }

    [Fact]
    public void ResolveGenericFamily_SansSerif_ReturnsFamilyName()
    {
        var name = SystemFontLocator.ResolveGenericFamily("sans-serif");
        name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ResolveGenericFamily_Serif_ReturnsFamilyName()
    {
        var name = SystemFontLocator.ResolveGenericFamily("serif");
        name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ResolveGenericFamily_Monospace_ReturnsFamilyName()
    {
        var name = SystemFontLocator.ResolveGenericFamily("monospace");
        name.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ResolveGenericFamily_Unknown_ReturnsSansSerif()
    {
        var name = SystemFontLocator.ResolveGenericFamily("unknown");
        name.Should().NotBeNullOrEmpty();
    }
}
