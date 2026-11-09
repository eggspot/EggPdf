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
        float trimOffset = CropMarks ? MarkLength + MarkOffset + bleed : bleed;
        float mediaWidth = pageWidthPt + 2 * trimOffset;
        float mediaHeight = pageHeightPt + 2 * trimOffset;

        string mediaBox = $"/MediaBox [0 0 {F(mediaWidth)} {F(mediaHeight)}]";
        string trimBox = $"/TrimBox [{F(trimOffset)} {F(trimOffset)} {F(trimOffset + pageWidthPt)} {F(trimOffset + pageHeightPt)}]";
        string bleedBox = $"/BleedBox [{F(trimOffset - bleed)} {F(trimOffset - bleed)} {F(trimOffset + pageWidthPt + bleed)} {F(trimOffset + pageHeightPt + bleed)}]";

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
        sb.AppendOpLine("q");
        sb.AppendOpLine(BlackStroke);
        sb.AppendOpLine(HairlineWidth);

        float left = markArea, right = markArea + pageWidthPt, bottom = markArea, top = markArea + pageHeightPt;
        DrawCornerMark(sb, left, top, MarkLength, MarkOffset);
        DrawCornerMark(sb, right, top, MarkLength, MarkOffset);
        DrawCornerMark(sb, left, bottom, MarkLength, MarkOffset);
        DrawCornerMark(sb, right, bottom, MarkLength, MarkOffset);

        sb.AppendOpLine("Q");
        return sb.ToString();
    }

    private const string BlackStroke = "0 0 0 RG";
    private const string HairlineWidth = "0.25 w";

    private static void DrawCornerMark(StringBuilder sb, float x, float y, float length, float gap)
    {
        sb.AppendOpLine($"{F(x - gap - length)} {F(y)} m {F(x - gap)} {F(y)} l S");
        sb.AppendOpLine($"{F(x + gap)} {F(y)} m {F(x + gap + length)} {F(y)} l S");
        sb.AppendOpLine($"{F(x)} {F(y - gap - length)} m {F(x)} {F(y - gap)} l S");
        sb.AppendOpLine($"{F(x)} {F(y + gap)} m {F(x)} {F(y + gap + length)} l S");
    }

    private static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
}
