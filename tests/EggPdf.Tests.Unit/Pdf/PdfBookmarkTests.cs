using System.Collections.Generic;
using System.Text;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfBookmarkTests
{
    [Fact]
    public void SingleH1_CreatesBookmark()
    {
        var doc = new PdfDocument();
        doc.AddPage(595.28f, 841.89f);
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
        doc.AddPage(595.28f, 841.89f);
        doc.SetBookmarks(new List<PdfBookmark>
        {
            new PdfBookmark { Title = "Chapter 1", Level = 1, PageIndex = 0, TopPt = 800f },
            new PdfBookmark { Title = "Section 1.1", Level = 2, PageIndex = 0, TopPt = 700f },
            new PdfBookmark { Title = "Section 1.2", Level = 2, PageIndex = 0, TopPt = 500f },
            new PdfBookmark { Title = "Chapter 2", Level = 1, PageIndex = 0, TopPt = 300f },
        });

        var text = Encoding.ASCII.GetString(doc.ToByteArray());

        // Should have outline root and items
        text.Should().Contain("/Type /Outlines");
        text.Should().Contain("Chapter 1");
        text.Should().Contain("Section 1.1");
        text.Should().Contain("Section 1.2");
        text.Should().Contain("Chapter 2");

        // Nested items should have /Parent pointing to their parent
        // Chapter 1 should have /First and /Last for its children
        text.Should().Contain("/First");
        text.Should().Contain("/Last");

        // Siblings should have /Next and /Prev
        text.Should().Contain("/Next");
        text.Should().Contain("/Prev");
    }

    [Fact]
    public void HeadingsOnMultiplePages_BookmarksPointToCorrectPages()
    {
        var doc = new PdfDocument();
        var page1 = doc.AddPage(595.28f, 841.89f);
        var page2 = doc.AddPage(595.28f, 841.89f);
        doc.SetBookmarks(new List<PdfBookmark>
        {
            new PdfBookmark { Title = "Page 1 Heading", Level = 1, PageIndex = 0, TopPt = 800f },
            new PdfBookmark { Title = "Page 2 Heading", Level = 1, PageIndex = 1, TopPt = 750f },
        });

        var bytes = doc.ToByteArray();
        var text = Encoding.ASCII.GetString(bytes);

        text.Should().Contain("Page 1 Heading");
        text.Should().Contain("Page 2 Heading");
        text.Should().Contain("/Outlines");

        // Both should have /Dest entries with /XYZ
        // Count XYZ occurrences - should be at least 2 (one per bookmark)
        int xyzCount = 0;
        int idx = 0;
        while ((idx = text.IndexOf("/XYZ", idx)) >= 0)
        {
            xyzCount++;
            idx += 4;
        }
        xyzCount.Should().BeGreaterOrEqualTo(2);
    }

    [Fact]
    public void NoHeadings_NoOutlines()
    {
        var doc = new PdfDocument();
        doc.AddPage(595.28f, 841.89f);
        // Don't set any bookmarks

        var text = Encoding.ASCII.GetString(doc.ToByteArray());

        text.Should().NotContain("/Outlines");
        text.Should().NotContain("/Type /Outlines");
    }

    [Fact]
    public void EmptyBookmarkList_NoOutlines()
    {
        var doc = new PdfDocument();
        doc.AddPage(595.28f, 841.89f);
        doc.SetBookmarks(new List<PdfBookmark>());

        var text = Encoding.ASCII.GetString(doc.ToByteArray());

        text.Should().NotContain("/Outlines");
    }

    [Fact]
    public void BookmarkTitleEncoded_SpecialCharacters()
    {
        var doc = new PdfDocument();
        doc.AddPage(595.28f, 841.89f);
        doc.SetBookmarks(new List<PdfBookmark>
        {
            new PdfBookmark { Title = "Title with (parens) and \\backslash", Level = 1, PageIndex = 0, TopPt = 800f }
        });

        var text = Encoding.ASCII.GetString(doc.ToByteArray());

        // Parentheses and backslashes should be escaped
        text.Should().Contain("\\(parens\\)");
        text.Should().Contain("\\\\backslash");
        text.Should().Contain("/Outlines");
    }

    [Fact]
    public void ThreeLevelHierarchy_CorrectNesting()
    {
        var doc = new PdfDocument();
        doc.AddPage(595.28f, 841.89f);
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

        // The outline root should have Count 1 (one top-level item)
        // H1 has 1 direct child (H2) + 1 grandchild (H3) = Count 2
        // H2 has 1 child (H3) = Count 1
        text.Should().Contain("/Count 1");
        text.Should().Contain("/Count 2");
    }

    [Fact]
    public void ValidPdf_WithBookmarks_HasCorrectStructure()
    {
        var doc = new PdfDocument();
        doc.AddPage(595.28f, 841.89f);
        doc.SetBookmarks(new List<PdfBookmark>
        {
            new PdfBookmark { Title = "Intro", Level = 1, PageIndex = 0, TopPt = 800f }
        });

        var bytes = doc.ToByteArray();

        // Should still be a valid PDF
        var header = Encoding.ASCII.GetString(bytes, 0, 8);
        header.Should().StartWith("%PDF-1.7");

        var text = Encoding.ASCII.GetString(bytes);
        text.Should().Contain("xref");
        text.Should().Contain("trailer");
        text.Should().Contain("%%EOF");
    }
}
