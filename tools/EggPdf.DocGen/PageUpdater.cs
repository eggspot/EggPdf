namespace EggPdf.DocGen;

/// <summary>Swaps the generated block (between the begin/end markers) inside the page's HTML.</summary>
public static class PageUpdater
{
    public static string Replace(string page, string generatedBlock)
    {
        int begin = page.IndexOf(ApiReferenceGenerator.BeginMarker, StringComparison.Ordinal);
        int end = page.IndexOf(ApiReferenceGenerator.EndMarker, StringComparison.Ordinal);
        if (begin < 0 || end < begin)
            throw new InvalidOperationException("The page has no generated-API-reference markers to replace.");

        int endOfBlock = end + ApiReferenceGenerator.EndMarker.Length;
        int lineStart = page.LastIndexOf('\n', begin) + 1;

        // Write the block with the page's own line endings so a CRLF checkout stays consistently CRLF.
        var newline = page.Contains("\r\n") ? "\r\n" : "\n";
        var block = generatedBlock.Replace("\r\n", "\n").TrimEnd('\n').Replace("\n", newline);
        return page.Substring(0, lineStart) + block + page.Substring(endOfBlock);
    }

    /// <summary>The text currently between (and including) the markers, for staleness comparison.</summary>
    public static string Extract(string page)
    {
        int begin = page.IndexOf(ApiReferenceGenerator.BeginMarker, StringComparison.Ordinal);
        int end = page.IndexOf(ApiReferenceGenerator.EndMarker, StringComparison.Ordinal);
        if (begin < 0 || end < begin) return "";
        int lineStart = page.LastIndexOf('\n', begin) + 1;
        return page.Substring(lineStart, end + ApiReferenceGenerator.EndMarker.Length - lineStart);
    }
}
