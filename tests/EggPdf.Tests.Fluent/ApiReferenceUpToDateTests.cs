using System;
using System.IO;
using EggPdf.DocGen;
using EggPdf.Fluent;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Fluent;

/// <summary>
/// The "API reference" in site/fluent-api.html is generated from the code and its XML doc comments
/// (tools/EggPdf.DocGen). These fail when a public member has no &lt;summary&gt; (generation throws)
/// or when the committed page differs from what the code now produces -- fix by running
/// <c>dotnet run --project tools/EggPdf.DocGen</c> and committing the page.
/// </summary>
public class ApiReferenceUpToDateTests
{
    private static string LoadPage()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "site", "fluent-api.html");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException("site/fluent-api.html not found above " + AppContext.BaseDirectory);
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n");

    [Fact]
    public void EveryPublicTypeAndMember_HasAnXmlSummary()
    {
        // Generate throws, listing every undocumented public type/member.
        Action generate = () => ApiReferenceGenerator.Generate(typeof(Document).Assembly, "EggPdf.Fluent");

        generate.Should().NotThrow();
    }

    [Fact]
    public void PublishedReference_MatchesTheCurrentCodeAndItsSummaries()
    {
        var generated = ApiReferenceGenerator.Generate(typeof(Document).Assembly, "EggPdf.Fluent");
        var published = PageUpdater.Extract(LoadPage());

        Normalize(published).Should().Be(Normalize(generated).TrimEnd('\n'),
            "site/fluent-api.html is out of date -- run: dotnet run --project tools/EggPdf.DocGen");
    }

    [Fact]
    public void Reference_DocumentsEveryTypeAndListsEveryEnumValue()
    {
        var generated = ApiReferenceGenerator.Generate(typeof(Document).Assembly, "EggPdf.Fluent");

        foreach (var name in new[] { "Document", "Container", "PageDescriptor", "Color", "Length", "GridTrack" })
            generated.Should().Contain("id=\"ref-" + name + "\"");
        foreach (var value in Enum.GetNames(typeof(FlexJustify)))
            generated.Should().Contain("<code>" + value + "</code>");
        generated.Should().Contain("BorderLineStyle.Solid", "enum defaults render as names, not numbers");
    }
}
