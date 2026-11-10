using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfBookmarkTests
{
    private const float A4Width = 595.28f, A4Height = 841.89f;

    [Fact]
    public void SingleH1_CreatesBookmark()
    {
        var doc = new PdfDocument();
        doc.AddPage(A4Width, A4Height);
        doc.SetBookmarks(new List<PdfBookmark>
        {
            new PdfBookmark { Title = "Chapter 1", Level = 1, PageIndex = 0, TopPt = 800f }
        });

        var text = Encoding.ASCII.GetString(doc.ToByteArray());

        text.Should().Contain("/Outlines");
        text.Should().Contain("/Type /Outlines");
        text.Should().Contain("Chapter 1");
        text.Should().Contain("/XYZ");
    }

    [Fact]
    public void MultipleHeadings_CreateNestedBookmarks()
    {
        var doc = new PdfDocument();
        doc.AddPage(A4Width, A4Height);
        doc.SetBookmarks(new List<PdfBookmark>
        {
            new PdfBookmark { Title = "Chapter 1", Level = 1, PageIndex = 0, TopPt = 800f },
            new PdfBookmark { Title = "Section 1.1", Level = 2, PageIndex = 0, TopPt = 700f },
            new PdfBookmark { Title = "Section 1.2", Level = 2, PageIndex = 0, TopPt = 500f },
            new PdfBookmark { Title = "Chapter 2", Level = 1, PageIndex = 0, TopPt = 300f },
        });

        var text = Encoding.ASCII.GetString(doc.ToByteArray());

        text.Should().Contain("/Type /Outlines");
        text.Should().Contain("Chapter 1");
        text.Should().Contain("Section 1.1");
        text.Should().Contain("Section 1.2");
        text.Should().Contain("Chapter 2");
        text.Should().Contain("/First", "Chapter 1 points at its first and last sections");
        text.Should().Contain("/Last");
        text.Should().Contain("/Next", "siblings link to each other");
        text.Should().Contain("/Prev");
    }

    [Fact]
    public void HeadingsOnMultiplePages_BookmarksPointToCorrectPages()
    {
        var doc = new PdfDocument();
        doc.AddPage(A4Width, A4Height);
        doc.AddPage(A4Width, A4Height);
        doc.SetBookmarks(new List<PdfBookmark>
        {
            new PdfBookmark { Title = "Page 1 Heading", Level = 1, PageIndex = 0, TopPt = 800f },
            new PdfBookmark { Title = "Page 2 Heading", Level = 1, PageIndex = 1, TopPt = 750f },
        });

        var text = Encoding.ASCII.GetString(doc.ToByteArray());

        text.Should().Contain("Page 1 Heading");
        text.Should().Contain("Page 2 Heading");
        var kids = Regex.Match(text, @"/Kids \[(\d+) 0 R (\d+) 0 R\]");
        kids.Success.Should().BeTrue("the page tree lists both pages");
        var destinations = Regex.Matches(text, @"/Dest \[(\d+) 0 R /XYZ 0 ([\d.]+) 0\]");
        destinations.Should().HaveCount(2, "each bookmark has an /XYZ destination");
        destinations[0].Groups[1].Value.Should().Be(kids.Groups[1].Value, "the first heading is on the first page");
        destinations[0].Groups[2].Value.Should().Be("800.00");
        destinations[1].Groups[1].Value.Should().Be(kids.Groups[2].Value, "the second heading is on the second page");
        destinations[1].Groups[2].Value.Should().Be("750.00");
    }

    [Fact]
    public void NoHeadings_NoOutlines()
    {
        var doc = new PdfDocument();
        doc.AddPage(A4Width, A4Height);

        var text = Encoding.ASCII.GetString(doc.ToByteArray());

        text.Should().NotContain("/Outlines");
        text.Should().NotContain("/Type /Outlines");
    }

    [Fact]
    public void EmptyBookmarkList_NoOutlines()
    {
        var doc = new PdfDocument();
        doc.AddPage(A4Width, A4Height);
        doc.SetBookmarks(new List<PdfBookmark>());

        var text = Encoding.ASCII.GetString(doc.ToByteArray());

        text.Should().NotContain("/Outlines");
    }

    [Fact]
    public void BookmarkTitleEncoded_SpecialCharacters()
    {
        var doc = new PdfDocument();
        doc.AddPage(A4Width, A4Height);
        doc.SetBookmarks(new List<PdfBookmark>
        {
            new PdfBookmark { Title = "Title with (parens) and \\backslash", Level = 1, PageIndex = 0, TopPt = 800f }
        });

        var text = Encoding.ASCII.GetString(doc.ToByteArray());

        text.Should().Contain("\\(parens\\)", "parentheses are escaped in a PDF literal string");
        text.Should().Contain("\\\\backslash", "so is the backslash");
        text.Should().Contain("/Outlines");
    }

    [Fact]
    public void ThreeLevelHierarchy_CorrectNesting()
    {
        var doc = new PdfDocument();
        doc.AddPage(A4Width, A4Height);
        doc.SetBookmarks(new List<PdfBookmark>
        {
            new PdfBookmark { Title = "H1", Level = 1, PageIndex = 0, TopPt = 800f },
            new PdfBookmark { Title = "H2", Level = 2, PageIndex = 0, TopPt = 700f },
            new PdfBookmark { Title = "H3", Level = 3, PageIndex = 0, TopPt = 600f },
        });

        var text = Encoding.ASCII.GetString(doc.ToByteArray());

        text.Should().Contain("/Type /Outlines");
        text.Should().Contain("H1");
        text.Should().Contain("H2");
        text.Should().Contain("H3");
        text.Should().Contain("/Count 2", "H1 counts its child H2 and its grandchild H3");
        text.Should().Contain("/Count 1", "the root counts its one top-level item, and H2 its one child");
    }

    [Fact]
    public void ValidPdf_WithBookmarks_HasCorrectStructure()
    {
        var doc = new PdfDocument();
        doc.AddPage(A4Width, A4Height);
        doc.SetBookmarks(new List<PdfBookmark>
        {
            new PdfBookmark { Title = "Intro", Level = 1, PageIndex = 0, TopPt = 800f }
        });

        var bytes = doc.ToByteArray();

        Encoding.ASCII.GetString(bytes, 0, 8).Should().StartWith("%PDF-1.7");
        var text = Encoding.ASCII.GetString(bytes);
        text.Should().Contain("xref");
        text.Should().Contain("trailer");
        text.Should().Contain("%%EOF");
    }
}
