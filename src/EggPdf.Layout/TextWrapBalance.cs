using System;

namespace EggPdf.Layout;

/// <summary>
/// CSS text-wrap: balance implementation (Chrome 114+).
/// Balances text across multiple lines to avoid orphaned short last lines.
/// Primarily useful for headings where visual balance matters.
/// </summary>
public static class TextWrapBalance
{
    /// <summary>
    /// Calculate the optimal line width for balanced text wrapping.
    /// Instead of filling each line to the maximum width, reduces the
    /// available width so lines are approximately equal length.
    /// </summary>
    /// <param name="totalTextWidth">Total text width if rendered on one line.</param>
    /// <param name="maxLineWidth">Maximum available line width.</param>
    /// <param name="lineCount">Number of lines the text wraps to at max width.</param>
    /// <returns>Optimal width for balanced lines.</returns>
    public static float CalculateBalancedWidth(float totalTextWidth, float maxLineWidth, int lineCount)
    {
        if (lineCount <= 1 || totalTextWidth <= maxLineWidth)
            return maxLineWidth;

        // Target: distribute text evenly across lines
        // Optimal width = totalTextWidth / lineCount + small margin
        float idealWidth = totalTextWidth / lineCount;

        // Add 5% margin to avoid excessive wrapping from rounding
        idealWidth *= 1.05f;

        // Don't exceed max width
        return Math.Min(idealWidth, maxLineWidth);
    }

    /// <summary>
    /// Check if text-wrap: balance should be applied.
    /// Only applies to elements with <= 10 lines (per spec).
    /// </summary>
    public static bool ShouldBalance(string? textWrap, int lineCount)
    {
        if (textWrap != "balance") return false;
        if (lineCount > 10) return false; // Spec limits to 10 lines
        return lineCount > 1;
    }
}
