namespace EggPdf.Pdf;

public partial class PdfPage
{
    internal void AddRoundedBorderSide(float x, float y, float w, float h, CornerRadii radii,
        float topWidth, float rightWidth, float bottomWidth, float leftWidth,
        BorderSide side, float r, float g, float b, string borderStyle)
    {
        float sideWidth = side switch
        {
            BorderSide.Top => topWidth,
            BorderSide.Right => rightWidth,
            BorderSide.Bottom => bottomWidth,
            _ => leftWidth
        };
        if (sideWidth <= 0f) return;

        radii = radii.FittedTo(w, h);
        ContentStream.AppendOpLine("q");
        AppendSideClip(x, y, w, h, topWidth, rightWidth, bottomWidth, leftWidth, side);
        var color = (r, g, b);

        switch (borderStyle)
        {
            case "dashed":
                ContentStream.AppendOpLine($"[{F(sideWidth * 3f)} {F(sideWidth * 3f)}] 0 d");
                ContentStream.AppendOpLine(ButtCap);
                StrokeRoundedRing(x, y, w, h, radii, sideWidth / 2f, sideWidth, color);
                break;

            case "dotted":
                ContentStream.AppendOpLine($"[0 {F(sideWidth * 2f)}] 0 d");
                ContentStream.AppendOpLine(RoundCap);
                StrokeRoundedRing(x, y, w, h, radii, sideWidth / 2f, sideWidth, color);
                break;

            case "double":
            {
                float third = sideWidth / 3f;
                StrokeRoundedRing(x, y, w, h, radii, third / 2f, third, color);
                StrokeRoundedRing(x, y, w, h, radii, sideWidth - third / 2f, third, color);
                break;
            }

            case "inset":
            case "outset":
                StrokeRoundedRing(x, y, w, h, radii, sideWidth / 2f, sideWidth,
                    BorderShade.InsetOutsetColor(BorderShade.DarkensSide(side, borderStyle == "inset"), r, g, b));
                break;

            case "groove":
            case "ridge":
            {
                bool outerHalfInset = borderStyle == "groove";
                float half = sideWidth / 2f;
                StrokeRoundedRing(x, y, w, h, radii, half / 2f, half,
                    BorderShade.InsetOutsetColor(BorderShade.DarkensSide(side, outerHalfInset), r, g, b));
                StrokeRoundedRing(x, y, w, h, radii, half + half / 2f, half,
                    BorderShade.InsetOutsetColor(BorderShade.DarkensSide(side, !outerHalfInset), r, g, b));
                break;
            }

            default:
                StrokeRoundedRing(x, y, w, h, radii, sideWidth / 2f, sideWidth, color);
                break;
        }

        ContentStream.AppendOpLine("Q");
    }

    private void AppendSideClip(float x, float y, float w, float h,
        float topWidth, float rightWidth, float bottomWidth, float leftWidth, BorderSide side)
    {
        float top = y + h, right = x + w;
        float innerLeft = x + leftWidth, innerRight = right - rightWidth;
        float innerBottom = y + bottomWidth, innerTop = top - topWidth;
        var (ax, ay, bx, by, cx, cy, dx, dy) = side switch
        {
            BorderSide.Top => (x, top, right, top, innerRight, innerTop, innerLeft, innerTop),
            BorderSide.Right => (right, top, right, y, innerRight, innerBottom, innerRight, innerTop),
            BorderSide.Bottom => (right, y, x, y, innerLeft, innerBottom, innerRight, innerBottom),
            _ => (x, y, x, top, innerLeft, innerTop, innerLeft, innerBottom)
        };
        ContentStream.AppendOpLine($"{F(ax)} {F(ay)} m {F(bx)} {F(by)} l {F(cx)} {F(cy)} l {F(dx)} {F(dy)} l h W n");
    }

    private void StrokeRoundedRing(float x, float y, float w, float h, CornerRadii radii, float inset, float lineWidth,
        (float R, float G, float B) color)
    {
        ContentStream.AppendOpLine($"{F(lineWidth)} w");
        ContentStream.AppendOpLine($"{F(color.R)} {F(color.G)} {F(color.B)} RG");
        AppendRoundedRectPath(x + inset, y + inset, w - 2f * inset, h - 2f * inset, radii.Expanded(-inset));
        ContentStream.AppendOpLine("h S");
    }
}
