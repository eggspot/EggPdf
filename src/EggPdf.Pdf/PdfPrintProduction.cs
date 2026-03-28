using System;
using System.Globalization;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// PDF print production features for commercial printing:
/// - TrimBox / BleedBox / CropBox page geometry
/// - Crop marks and registration marks
/// - Bleed area handling
/// </summary>
public class PdfPrintProduction
{
    /// <summary>Bleed size in points (typically 3mm = ~8.5pt).</summary>
    public float BleedPt { get; set; } = 8.5f;

    /// <summary>Whether to add crop marks.</summary>
    public bool CropMarks { get; set; }

    /// <summary>Whether to add registration marks.</summary>
    public bool RegistrationMarks { get; set; }

    /// <summary>Length of crop marks in points.</summary>
    public float MarkLength { get; set; } = 18f;

    /// <summary>Offset from trim edge to mark start in points.</summary>
    public float MarkOffset { get; set; } = 6f;

    /// <summary>
    /// Generate the page box definitions for a page.
    /// Returns (MediaBox, TrimBox, BleedBox) as PDF dictionary entries.
    /// </summary>
    public (string mediaBox, string? trimBox, string? bleedBox) GetPageBoxes(float pageWidthPt, float pageHeightPt)
    {
        float bleed = BleedPt;

        // MediaBox includes bleed + mark area
        float markArea = CropMarks ? MarkLength + MarkOffset + bleed : bleed;
        float mediaW = pageWidthPt + 2 * markArea;
        float mediaH = pageHeightPt + 2 * markArea;

        string mediaBox = $"/MediaBox [0 0 {F(mediaW)} {F(mediaH)}]";

        // TrimBox = the intended final page size
        string trimBox = $"/TrimBox [{F(markArea)} {F(markArea)} {F(markArea + pageWidthPt)} {F(markArea + pageHeightPt)}]";

        // BleedBox = TrimBox expanded by bleed amount
        string bleedBox = $"/BleedBox [{F(markArea - bleed)} {F(markArea - bleed)} {F(markArea + pageWidthPt + bleed)} {F(markArea + pageHeightPt + bleed)}]";

        return (mediaBox, trimBox, bleedBox);
    }

    /// <summary>
    /// Generate PDF content stream commands for crop marks at page corners.
    /// </summary>
    public string GenerateCropMarks(float pageWidthPt, float pageHeightPt)
    {
        if (!CropMarks) return "";

        float bleed = BleedPt;
        float markArea = MarkLength + MarkOffset + bleed;

        var sb = new StringBuilder();
        sb.AppendLine("q");
        sb.AppendLine("0 0 0 RG"); // Black stroke
        sb.AppendLine("0.25 w");    // Thin line

        // Top-left corner
        DrawCornerMark(sb, markArea, markArea + pageHeightPt, markArea, MarkLength, MarkOffset);

        // Top-right corner
        DrawCornerMark(sb, markArea + pageWidthPt, markArea + pageHeightPt, markArea, MarkLength, MarkOffset);

        // Bottom-left corner
        DrawCornerMark(sb, markArea, markArea, markArea, MarkLength, MarkOffset);

        // Bottom-right corner
        DrawCornerMark(sb, markArea + pageWidthPt, markArea, markArea, MarkLength, MarkOffset);

        sb.AppendLine("Q");
        return sb.ToString();
    }

    private static void DrawCornerMark(StringBuilder sb, float x, float y, float offset, float length, float gap)
    {
        // Horizontal mark
        sb.AppendLine($"{F(x - gap - length)} {F(y)} m {F(x - gap)} {F(y)} l S");
        sb.AppendLine($"{F(x + gap)} {F(y)} m {F(x + gap + length)} {F(y)} l S");

        // Vertical mark
        sb.AppendLine($"{F(x)} {F(y - gap - length)} m {F(x)} {F(y - gap)} l S");
        sb.AppendLine($"{F(x)} {F(y + gap)} m {F(x)} {F(y + gap + length)} l S");
    }

    private static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
}
