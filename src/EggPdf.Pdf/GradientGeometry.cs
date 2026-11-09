using System;
using System.Globalization;
using System.Text;

namespace EggPdf.Pdf;

internal static class GradientGeometry
{
    private const float PxToPt = 0.75f;
    private const float EmToPt = 12f;
    private const float KappaCircle = 0.5522847498f;

    public static (float FromLeft, float FromTop) ParsePosition(string? text, float width, float height)
    {
        float fromLeft = width / 2f, fromTop = height / 2f;
        if (string.IsNullOrEmpty(text)) return (fromLeft, fromTop);

        var tokens = text!.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 1)
        {
            if (tokens[0] == "top" || tokens[0] == "bottom")
                fromTop = Offset(tokens[0], height);
            else
                fromLeft = Offset(tokens[0], width);
            return (fromLeft, fromTop);
        }

        if (tokens.Length >= 3) return EdgeOffsetPosition(tokens, width, height);

        string horizontal = tokens[0], vertical = tokens[1];
        if (horizontal == "top" || horizontal == "bottom" || vertical == "left" || vertical == "right")
            (horizontal, vertical) = (vertical, horizontal);
        return (Offset(horizontal, width), Offset(vertical, height));
    }

    private static (float FromLeft, float FromTop) EdgeOffsetPosition(string[] tokens, float width, float height)
    {
        float fromLeft = width / 2f, fromTop = height / 2f;
        for (int i = 0; i < tokens.Length; i++)
        {
            string edge = tokens[i];
            bool hasOffset = i + 1 < tokens.Length && !IsKeyword(tokens[i + 1]);
            float offset = hasOffset ? Length(tokens[++i], edge == "left" || edge == "right" ? width : height) : 0f;
            switch (edge)
            {
                case "left": fromLeft = offset; break;
                case "right": fromLeft = width - offset; break;
                case "top": fromTop = offset; break;
                case "bottom": fromTop = height - offset; break;
            }
        }
        return (fromLeft, fromTop);
    }

    private static bool IsKeyword(string token) =>
        token == "left" || token == "right" || token == "top" || token == "bottom" || token == "center";

    public static float Length(string token, float reference)
    {
        token = token.Trim().ToLowerInvariant();
        float factor;
        string number;
        if (token.EndsWith("%", StringComparison.Ordinal)) { factor = reference / 100f; number = token.Substring(0, token.Length - 1); }
        else if (token.EndsWith("px", StringComparison.Ordinal)) { factor = PxToPt; number = token.Substring(0, token.Length - 2); }
        else if (token.EndsWith("pt", StringComparison.Ordinal)) { factor = 1f; number = token.Substring(0, token.Length - 2); }
        else if (token.EndsWith("em", StringComparison.Ordinal)) { factor = EmToPt; number = token.Substring(0, token.Length - 2); }
        else { factor = 1f; number = token; }
        return float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value * factor : 0f;
    }

    public static void AppendEllipse(StringBuilder sb, float cx, float cy, float rx, float ry)
    {
        float kx = rx * KappaCircle, ky = ry * KappaCircle;
        sb.AppendOpLine($"{F(cx + rx)} {F(cy)} m");
        sb.AppendOpLine($"{F(cx + rx)} {F(cy + ky)} {F(cx + kx)} {F(cy + ry)} {F(cx)} {F(cy + ry)} c");
        sb.AppendOpLine($"{F(cx - kx)} {F(cy + ry)} {F(cx - rx)} {F(cy + ky)} {F(cx - rx)} {F(cy)} c");
        sb.AppendOpLine($"{F(cx - rx)} {F(cy - ky)} {F(cx - kx)} {F(cy - ry)} {F(cx)} {F(cy - ry)} c");
        sb.AppendOpLine($"{F(cx + kx)} {F(cy - ry)} {F(cx + rx)} {F(cy - ky)} {F(cx + rx)} {F(cy)} c h");
    }

    public static string F(float value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static float Offset(string token, float dimension)
    {
        switch (token)
        {
            case "left":
            case "top":
                return 0f;
            case "center":
                return dimension / 2f;
            case "right":
            case "bottom":
                return dimension;
            default:
                return Length(token, dimension);
        }
    }
}
