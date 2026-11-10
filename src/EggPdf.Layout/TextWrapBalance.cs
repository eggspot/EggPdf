using System;

namespace EggPdf.Layout;

/// <summary>
/// CSS text-wrap: balance implementation (Chrome 114+).
/// Balances text across multiple lines to avoid orphaned short last lines.
/// Primarily useful for headings where visual balance matters.
/// </summary>
public static class TextWrapBalance
{
    private const int MaxBalancedLines = 10;
    private const float WidthSlackForRounding = 1f;
    private const int SearchSteps = 12;

    /// <summary>
    /// Calculate the optimal line width for balanced text wrapping.
    /// Instead of filling each line to the maximum width, reduces the
    /// available width so lines are approximately equal length.
    /// </summary>
    /// <param name="totalTextWidth">Total text width if rendered on one line.</param>
    /// <param name="maxLineWidth">Maximum available line width.</param>
    /// <param name="lineCount">Number of lines the text wraps to at max width.</param>
    /// <returns>Optimal width for balanced lines.</returns>
    [Obsolete("No longer used by the layout engine: the average line width plus 5% often lets the greedy first line through unchanged. Layout now searches for the narrowest width that keeps the line count.")]
    public static float CalculateBalancedWidth(float totalTextWidth, float maxLineWidth, int lineCount)
    {
        if (lineCount <= 1 || totalTextWidth <= maxLineWidth)
            return maxLineWidth;

        return Math.Min(totalTextWidth / lineCount * 1.05f, maxLineWidth);
    }

    /// <summary>
    /// Check if text-wrap: balance should be applied.
    /// Only applies to elements with <= 10 lines (per spec).
    /// </summary>
    public static bool ShouldBalance(string? textWrap, int lineCount)
    {
        if (textWrap != "balance") return false;
        if (lineCount > MaxBalancedLines) return false;
        return lineCount > 1;
    }

    internal static float FindNarrowestWidthKeepingLineCount(float maxLineWidth, int lineCountAtMaxWidth,
        Func<float, int> countLinesAtWidth)
    {
        float tooNarrow = 0f, wideEnough = maxLineWidth;
        for (int step = 0; step < SearchSteps; step++)
        {
            float mid = (tooNarrow + wideEnough) / 2f;
            if (countLinesAtWidth(mid) <= lineCountAtMaxWidth) wideEnough = mid;
            else tooNarrow = mid;
        }
        return Math.Min(maxLineWidth, wideEnough + WidthSlackForRounding);
    }
}
