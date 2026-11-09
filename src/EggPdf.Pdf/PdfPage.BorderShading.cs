namespace EggPdf.Pdf;

public partial class PdfPage
{
    internal void AddBorderLine(float x1, float y1, float x2, float y2,
        float r, float g, float b, float lineWidth, string borderStyle, BorderSide side)
    {
        if (!IsThreeDimensionalBorder(borderStyle))
        {
            AddBorderLine(x1, y1, x2, y2, r, g, b, lineWidth, borderStyle);
            return;
        }

        var (outwardX, outwardY) = side switch
        {
            BorderSide.Top => (0f, 1f),
            BorderSide.Right => (1f, 0f),
            BorderSide.Bottom => (0f, -1f),
            _ => (-1f, 0f)
        };
        AddThreeDimensionalBorderLine(x1, y1, x2, y2, r, g, b, lineWidth, borderStyle, side, outwardX, outwardY);
    }

    private static bool IsThreeDimensionalBorder(string borderStyle) =>
        borderStyle == "inset" || borderStyle == "outset" || borderStyle == "groove" || borderStyle == "ridge";

    private void AddThreeDimensionalBorderLine(float x1, float y1, float x2, float y2,
        float r, float g, float b, float lineWidth, string borderStyle, BorderSide side, float outwardX, float outwardY)
    {
        if (borderStyle == "inset" || borderStyle == "outset")
        {
            var shade = BorderShade.InsetOutsetColor(BorderShade.DarkensSide(side, borderStyle == "inset"), r, g, b);
            StrokeBorderLine(x1, y1, x2, y2, shade, lineWidth);
            return;
        }

        bool outerHalfInset = borderStyle == "groove";
        var outer = BorderShade.InsetOutsetColor(BorderShade.DarkensSide(side, outerHalfInset), r, g, b);
        var inner = BorderShade.InsetOutsetColor(BorderShade.DarkensSide(side, !outerHalfInset), r, g, b);
        float half = lineWidth / 2f;
        float shiftX = outwardX * lineWidth / 4f, shiftY = outwardY * lineWidth / 4f;
        StrokeBorderLine(x1 + shiftX, y1 + shiftY, x2 + shiftX, y2 + shiftY, outer, half);
        StrokeBorderLine(x1 - shiftX, y1 - shiftY, x2 - shiftX, y2 - shiftY, inner, half);
    }

    private void StrokeBorderLine(float x1, float y1, float x2, float y2, (float R, float G, float B) color, float lineWidth)
    {
        ContentStream.AppendOpLine($"{F(lineWidth)} w");
        ContentStream.AppendOpLine($"{F(color.R)} {F(color.G)} {F(color.B)} RG");
        ContentStream.AppendOpLine($"{F(x1)} {F(y1)} m {F(x2)} {F(y2)} l S");
    }
}
