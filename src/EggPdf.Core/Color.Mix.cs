using System;
using System.Globalization;

namespace EggPdf.Core;

public readonly partial struct Color
{
    private enum MixSpace { Srgb, SrgbLinear, Oklab }

    /// <summary>
    /// color-mix([in &lt;space&gt;,] &lt;color&gt; [&lt;percentage&gt;], &lt;color&gt; [&lt;percentage&gt;]) per CSS Color 5:
    /// premultiplied-alpha interpolation, percentages normalised to 100%, and a sum below 100% scales the alpha.
    /// </summary>
    private static Color? TryParseColorMix(string value)
    {
        var inner = ExtractInner(value); if (inner == null) return null;
        var parts = SplitTopLevel(inner, ',');

        MixSpace space;
        int first;
        if (parts.Count == 3)
        {
            if (!TryParseMixSpace(parts[0], out space)) return null;
            first = 1;
        }
        else if (parts.Count == 2)
        {
            space = MixSpace.Oklab;
            first = 0;
        }
        else
        {
            return null;
        }

        if (!TryParseMixComponent(parts[first], out var c1, out var p1) ||
            !TryParseMixComponent(parts[first + 1], out var c2, out var p2))
            return null;

        double w1, w2;
        if (p1.HasValue && p2.HasValue) { w1 = p1.Value; w2 = p2.Value; }
        else if (p1.HasValue) { w1 = p1.Value; w2 = 1 - w1; }
        else if (p2.HasValue) { w2 = p2.Value; w1 = 1 - w2; }
        else { w1 = w2 = 0.5; }

        double sum = w1 + w2;
        if (sum <= 0) return null;
        double alphaMultiplier = Math.Min(sum, 1.0);
        w1 /= sum;
        w2 /= sum;

        ToMixSpace(c1, space, out double x1, out double y1, out double z1);
        ToMixSpace(c2, space, out double x2, out double y2, out double z2);
        double a1 = c1.A / 255.0, a2 = c2.A / 255.0;
        double alpha = a1 * w1 + a2 * w2;
        double x = 0, y = 0, z = 0;
        if (alpha > 0)
        {
            x = (x1 * a1 * w1 + x2 * a2 * w2) / alpha;
            y = (y1 * a1 * w1 + y2 * a2 * w2) / alpha;
            z = (z1 * a1 * w1 + z2 * a2 * w2) / alpha;
        }

        FromMixSpace(space, x, y, z, out double r, out double g, out double b);
        return FromUnitRgb(r, g, b, (float)(alpha * alphaMultiplier));
    }

    /// <summary>
    /// The interpolation space. srgb and srgb-linear are exact; xyz spaces are linear light, so they mix like srgb-linear;
    /// the other RGB spaces and hsl/hwb mix in srgb, and lab/lch/oklch in oklab -- the closest space this engine models.
    /// </summary>
    private static bool TryParseMixSpace(string clause, out MixSpace space)
    {
        space = MixSpace.Oklab;
        var words = clause.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2 || !string.Equals(words[0], "in", StringComparison.OrdinalIgnoreCase))
            return false;

        switch (words[1].ToLowerInvariant())
        {
            case "srgb":
            case "display-p3":
            case "a98-rgb":
            case "prophoto-rgb":
            case "rec2020":
            case "hsl":
            case "hwb":
                space = MixSpace.Srgb;
                return true;
            case "srgb-linear":
            case "xyz":
            case "xyz-d50":
            case "xyz-d65":
                space = MixSpace.SrgbLinear;
                return true;
            case "oklab":
            case "oklch":
            case "lab":
            case "lch":
                space = MixSpace.Oklab;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseMixComponent(string token, out Color color, out double? percentage)
    {
        color = default;
        percentage = null;
        token = token.Trim();
        string colorText = token;

        int lastSpace = token.LastIndexOf(' ');
        int firstSpace = token.IndexOf(' ');
        if (lastSpace > 0 && EndsWithPercent(token) && token.IndexOf(')', lastSpace) < 0 &&
            TryParsePercentage(token.Substring(lastSpace + 1), out double trailing))
        {
            percentage = trailing;
            colorText = token.Substring(0, lastSpace);
        }
        else if (firstSpace > 0 && token[firstSpace - 1] == '%' &&
                 TryParsePercentage(token.Substring(0, firstSpace), out double leading))
        {
            percentage = leading;
            colorText = token.Substring(firstSpace + 1);
        }

        if (percentage.HasValue && (percentage.Value < 0 || percentage.Value > 1))
            return false;

        var parsed = TryParse(colorText);
        if (!parsed.HasValue) return false;
        color = parsed.Value;
        return true;
    }

    private static bool TryParsePercentage(string text, out double fraction)
    {
        fraction = 0;
        text = text.Trim();
        if (!EndsWithPercent(text)) return false;
        if (!double.TryParse(text.Substring(0, text.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double pct))
            return false;
        fraction = pct / 100.0;
        return true;
    }

    private static void ToMixSpace(Color c, MixSpace space, out double x, out double y, out double z)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        switch (space)
        {
            case MixSpace.SrgbLinear:
                x = SrgbTransferToLinear(r); y = SrgbTransferToLinear(g); z = SrgbTransferToLinear(b);
                break;
            case MixSpace.Oklab:
                SrgbToOklab(r, g, b, out x, out y, out z);
                break;
            default:
                x = r; y = g; z = b;
                break;
        }
    }

    private static void FromMixSpace(MixSpace space, double x, double y, double z, out double r, out double g, out double b)
    {
        switch (space)
        {
            case MixSpace.SrgbLinear:
                r = LinearToSrgbTransfer(x); g = LinearToSrgbTransfer(y); b = LinearToSrgbTransfer(z);
                break;
            case MixSpace.Oklab:
                OklabToSrgb(x, y, z, out r, out g, out b);
                break;
            default:
                r = x; g = y; b = z;
                break;
        }
    }
}
