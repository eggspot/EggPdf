using System;

namespace EggPdf.Core;

public readonly partial struct Color
{
    private static readonly double[] XyzD65ToLinearSrgb =
    {
        3.2409699419045226, -1.537383177570094, -0.4986107602930034,
        -0.9692436362808796, 1.8759675015077202, 0.04155505740717559,
        0.05563007969699366, -0.20397695888897652, 1.0569715142428786,
    };

    private static readonly double[] LinearDisplayP3ToXyzD65 =
    {
        0.4865709486482162, 0.26566769316909306, 0.1982172852343625,
        0.2289745640697488, 0.6917385218365064, 0.079286914093745,
        0.0, 0.04511338185890264, 1.043944368900976,
    };

    private static readonly double[] LinearA98RgbToXyzD65 =
    {
        0.5766690429101305, 0.1855582379065463, 0.1882286462349947,
        0.29734497525053605, 0.6273635662554661, 0.07529145849399788,
        0.02703136138641234, 0.07068885253582723, 0.9913375368376388,
    };

    private static readonly double[] LinearRec2020ToXyzD65 =
    {
        0.6369580483012914, 0.14461690358620832, 0.1688809751641721,
        0.2627002120112671, 0.6779980715188708, 0.05930171646986196,
        0.0, 0.028072693049087428, 1.060985057710791,
    };

    private static readonly double[] LinearProPhotoToXyzD50 =
    {
        0.7977604896723027, 0.13518583717574031, 0.0313493495815248,
        0.2880711282292934, 0.7118432178101014, 0.00008565396060525902,
        0.0, 0.0, 0.8251046025104601,
    };

    private static readonly double[] BradfordD50ToD65 =
    {
        0.955473421488075, -0.02309845494876471, 0.06325924320057072,
        -0.0283697093338637, 1.0099953980813041, 0.021041441191917323,
        0.012314014864481998, -0.020507649298898964, 1.330365926242124,
    };

    private const double A98Gamma = 563.0 / 256.0;
    private const double Rec2020Alpha = 1.09929682680944;
    private const double Rec2020Beta = 0.018053968510807;
    private const double ProPhotoGamma = 1.8;
    private const double ProPhotoLinearLimit = 16.0 / 512.0;

    /// <summary>color(&lt;space&gt; c1 c2 c3 [/ alpha]) for the predefined CSS Color 4 spaces; an unknown space is invalid.</summary>
    private static Color? TryParseColorFunction(string value)
    {
        var inner = ExtractInner(value); if (inner == null) return null;
        float? alpha = SplitAlpha(ref inner);
        var parts = inner.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) return null;

        double c1 = ParseScaledComponent(parts[1], 1f);
        double c2 = ParseScaledComponent(parts[2], 1f);
        double c3 = ParseScaledComponent(parts[3], 1f);

        switch (parts[0].ToLowerInvariant())
        {
            case "srgb":
                return FromUnitRgb(c1, c2, c3, alpha);
            case "srgb-linear":
                return FromLinearSrgb(c1, c2, c3, alpha);
            case "display-p3":
                return FromLinearRgb(LinearDisplayP3ToXyzD65, SrgbTransferToLinear(c1), SrgbTransferToLinear(c2), SrgbTransferToLinear(c3), alpha);
            case "a98-rgb":
                return FromLinearRgb(LinearA98RgbToXyzD65, A98ToLinear(c1), A98ToLinear(c2), A98ToLinear(c3), alpha);
            case "rec2020":
                return FromLinearRgb(LinearRec2020ToXyzD65, Rec2020ToLinear(c1), Rec2020ToLinear(c2), Rec2020ToLinear(c3), alpha);
            case "prophoto-rgb":
            {
                Transform(LinearProPhotoToXyzD50, ProPhotoToLinear(c1), ProPhotoToLinear(c2), ProPhotoToLinear(c3), out double x, out double y, out double z);
                return FromXyzD50(x, y, z, alpha);
            }
            case "xyz":
            case "xyz-d65":
                return FromXyzD65(c1, c2, c3, alpha);
            case "xyz-d50":
                return FromXyzD50(c1, c2, c3, alpha);
            default:
                return null;
        }
    }

    private static Color FromLinearRgb(double[] toXyzD65, double r, double g, double b, float? alpha)
    {
        Transform(toXyzD65, r, g, b, out double x, out double y, out double z);
        return FromXyzD65(x, y, z, alpha);
    }

    private static Color FromXyzD50(double x, double y, double z, float? alpha)
    {
        Transform(BradfordD50ToD65, x, y, z, out double x65, out double y65, out double z65);
        return FromXyzD65(x65, y65, z65, alpha);
    }

    private static Color FromXyzD65(double x, double y, double z, float? alpha)
    {
        Transform(XyzD65ToLinearSrgb, x, y, z, out double r, out double g, out double b);
        return FromLinearSrgb(r, g, b, alpha);
    }

    private static Color FromLinearSrgb(double r, double g, double b, float? alpha)
        => FromUnitRgb(LinearToSrgbTransfer(r), LinearToSrgbTransfer(g), LinearToSrgbTransfer(b), alpha);

    private static void Transform(double[] m, double a, double b, double c, out double x, out double y, out double z)
    {
        x = m[0] * a + m[1] * b + m[2] * c;
        y = m[3] * a + m[4] * b + m[5] * c;
        z = m[6] * a + m[7] * b + m[8] * c;
    }

    private static double SrgbTransferToLinear(double c)
    {
        double abs = Math.Abs(c);
        double linear = abs <= 0.04045 ? abs / 12.92 : Math.Pow((abs + 0.055) / 1.055, 2.4);
        return c < 0 ? -linear : linear;
    }

    private static double LinearToSrgbTransfer(double c)
    {
        double abs = Math.Abs(c);
        double encoded = abs <= 0.0031308 ? abs * 12.92 : 1.055 * Math.Pow(abs, 1.0 / 2.4) - 0.055;
        return c < 0 ? -encoded : encoded;
    }

    private static double A98ToLinear(double c)
    {
        double linear = Math.Pow(Math.Abs(c), A98Gamma);
        return c < 0 ? -linear : linear;
    }

    private static double Rec2020ToLinear(double c)
    {
        double abs = Math.Abs(c);
        double linear = abs < Rec2020Beta * 4.5 ? abs / 4.5 : Math.Pow((abs + Rec2020Alpha - 1) / Rec2020Alpha, 1 / 0.45);
        return c < 0 ? -linear : linear;
    }

    private static double ProPhotoToLinear(double c)
    {
        double abs = Math.Abs(c);
        double linear = abs <= ProPhotoLinearLimit ? abs / 16.0 : Math.Pow(abs, ProPhotoGamma);
        return c < 0 ? -linear : linear;
    }

    private static void OklabToSrgb(double L, double a, double b, out double r, out double g, out double bl)
    {
        double l_ = L + 0.3963377774 * a + 0.2158037573 * b;
        double m_ = L - 0.1055613458 * a - 0.0638541728 * b;
        double s_ = L - 0.0894841775 * a - 1.2914855480 * b;
        double lc = l_ * l_ * l_;
        double mc = m_ * m_ * m_;
        double sc = s_ * s_ * s_;
        r = LinearToSrgbTransfer(4.0767416621 * lc - 3.3077115913 * mc + 0.2309699292 * sc);
        g = LinearToSrgbTransfer(-1.2684380046 * lc + 2.6097574011 * mc - 0.3413193965 * sc);
        bl = LinearToSrgbTransfer(-0.0041960863 * lc - 0.7034186147 * mc + 1.7076147010 * sc);
    }

    private static void SrgbToOklab(double r, double g, double b, out double L, out double a, out double bAxis)
    {
        double lr = SrgbTransferToLinear(r), lg = SrgbTransferToLinear(g), lb = SrgbTransferToLinear(b);
        double l = CubeRoot(0.4122214708 * lr + 0.5363325363 * lg + 0.0514459929 * lb);
        double m = CubeRoot(0.2119034982 * lr + 0.6806995451 * lg + 0.1073969566 * lb);
        double s = CubeRoot(0.0883024619 * lr + 0.2817188376 * lg + 0.6299787005 * lb);
        L = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s;
        a = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        bAxis = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;
    }

    private static double CubeRoot(double v) => v < 0 ? -Math.Pow(-v, 1.0 / 3.0) : Math.Pow(v, 1.0 / 3.0);

    /// <summary>CSS lab(): CIE Lab relative to the D50 white point, adapted to D65 with Bradford for sRGB.</summary>
    private static void CieLabToSrgb(double L, double a, double b, out double r, out double g, out double bl)
    {
        const double D50X = 0.3457 / 0.3585, D50Y = 1.0, D50Z = (1.0 - 0.3457 - 0.3585) / 0.3585;
        const double Epsilon = 6.0 / 29.0;
        double fy = (L + 16.0) / 116.0;
        double fx = a / 500.0 + fy;
        double fz = fy - b / 200.0;
        double x = (fx > Epsilon ? fx * fx * fx : 3 * Epsilon * Epsilon * (fx - 4.0 / 29.0)) * D50X;
        double y = (fy > Epsilon ? fy * fy * fy : 3 * Epsilon * Epsilon * (fy - 4.0 / 29.0)) * D50Y;
        double z = (fz > Epsilon ? fz * fz * fz : 3 * Epsilon * Epsilon * (fz - 4.0 / 29.0)) * D50Z;
        Transform(BradfordD50ToD65, x, y, z, out double x65, out double y65, out double z65);
        Transform(XyzD65ToLinearSrgb, x65, y65, z65, out double lr, out double lg, out double lb);
        r = LinearToSrgbTransfer(lr);
        g = LinearToSrgbTransfer(lg);
        bl = LinearToSrgbTransfer(lb);
    }
}
