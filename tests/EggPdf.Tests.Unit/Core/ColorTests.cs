using EggPdf.Core;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Core;

public class ColorTests
{
    [Fact]
    public void FromRgb_CreatesOpaqueColor()
    {
        var color = Color.FromRgb(255, 128, 0);

        color.R.Should().Be(255);
        color.G.Should().Be(128);
        color.B.Should().Be(0);
        color.A.Should().Be(255);
        color.IsOpaque.Should().BeTrue();
        color.IsTransparent.Should().BeFalse();
    }

    [Fact]
    public void FromRgba_CreatesTranslucentColor()
    {
        var color = Color.FromRgba(100, 200, 50, 128);

        color.R.Should().Be(100);
        color.G.Should().Be(200);
        color.B.Should().Be(50);
        color.A.Should().Be(128);
        color.IsOpaque.Should().BeFalse();
        color.IsTransparent.Should().BeFalse();
    }

    [Theory]
    [InlineData("#fff", 255, 255, 255)]
    [InlineData("#000", 0, 0, 0)]
    [InlineData("#f00", 255, 0, 0)]
    [InlineData("#FF0000", 255, 0, 0)]
    [InlineData("#00ff00", 0, 255, 0)]
    [InlineData("#0000FF", 0, 0, 255)]
    [InlineData("#abcdef", 171, 205, 239)]
    public void FromHex_ParsesRgb(string hex, byte r, byte g, byte b)
    {
        var color = Color.FromHex(hex);

        color.R.Should().Be(r);
        color.G.Should().Be(g);
        color.B.Should().Be(b);
        color.A.Should().Be(255);
    }

    [Theory]
    [InlineData("#ff000080", 255, 0, 0, 128)]
    [InlineData("#00ff0000", 0, 255, 0, 0)]
    public void FromHex_Parses8DigitWithAlpha(string hex, byte r, byte g, byte b, byte a)
    {
        var color = Color.FromHex(hex);

        color.R.Should().Be(r);
        color.G.Should().Be(g);
        color.B.Should().Be(b);
        color.A.Should().Be(a);
    }

    [Theory]
    [InlineData("#f008", 255, 0, 0, 136)]
    public void FromHex_Parses4DigitWithAlpha(string hex, byte r, byte g, byte b, byte a)
    {
        var color = Color.FromHex(hex);

        color.R.Should().Be(r);
        color.G.Should().Be(g);
        color.B.Should().Be(b);
        color.A.Should().Be(a);
    }

    [Theory]
    [InlineData("red", 255, 0, 0)]
    [InlineData("blue", 0, 0, 255)]
    [InlineData("green", 0, 128, 0)]
    [InlineData("white", 255, 255, 255)]
    [InlineData("black", 0, 0, 0)]
    [InlineData("rebeccapurple", 102, 51, 153)]
    [InlineData("RED", 255, 0, 0)]
    [InlineData("Red", 255, 0, 0)]
    public void FromName_ParsesNamedColors(string name, byte r, byte g, byte b)
    {
        var color = Color.FromName(name);

        color.R.Should().Be(r);
        color.G.Should().Be(g);
        color.B.Should().Be(b);
    }

    [Fact]
    public void FromName_UnknownName_Throws()
    {
        var act = () => Color.FromName("notacolor");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryParseNamed_UnknownName_ReturnsNull()
    {
        var result = Color.TryParseNamed("notacolor");

        result.Should().BeNull();
    }

    [Fact]
    public void Transparent_IsFullyTransparent()
    {
        Color.Transparent.A.Should().Be(0);
        Color.Transparent.IsTransparent.Should().BeTrue();
    }

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        var a = Color.FromRgb(10, 20, 30);
        var b = Color.FromRgb(10, 20, 30);

        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var a = Color.FromRgb(10, 20, 30);
        var b = Color.FromRgb(10, 20, 31);

        a.Equals(b).Should().BeFalse();
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void NamedColors_AllPresent()
    {
        // Verify all 148 CSS named colors are present
        var names = new[] { "aliceblue", "antiquewhite", "aqua", "aquamarine", "azure",
            "beige", "bisque", "black", "blanchedalmond", "blue", "blueviolet", "brown",
            "burlywood", "cadetblue", "chartreuse", "chocolate", "coral", "cornflowerblue",
            "cornsilk", "crimson", "cyan", "darkblue", "darkcyan", "darkgoldenrod",
            "darkgray", "darkgreen", "darkgrey", "darkkhaki", "darkmagenta",
            "darkolivegreen", "darkorange", "darkorchid", "darkred", "darksalmon",
            "darkseagreen", "darkslateblue", "darkslategray", "darkslategrey",
            "darkturquoise", "darkviolet", "deeppink", "deepskyblue", "dimgray", "dimgrey",
            "dodgerblue", "firebrick", "floralwhite", "forestgreen", "fuchsia", "gainsboro",
            "ghostwhite", "gold", "goldenrod", "gray", "green", "greenyellow", "grey",
            "honeydew", "hotpink", "indianred", "indigo", "ivory", "khaki", "lavender",
            "lavenderblush", "lawngreen", "lemonchiffon", "lightblue", "lightcoral",
            "lightcyan", "lightgoldenrodyellow", "lightgray", "lightgreen", "lightgrey",
            "lightpink", "lightsalmon", "lightseagreen", "lightskyblue", "lightslategray",
            "lightslategrey", "lightsteelblue", "lightyellow", "lime", "limegreen", "linen",
            "magenta", "maroon", "mediumaquamarine", "mediumblue", "mediumorchid",
            "mediumpurple", "mediumseagreen", "mediumslateblue", "mediumspringgreen",
            "mediumturquoise", "mediumvioletred", "midnightblue", "mintcream", "mistyrose",
            "moccasin", "navajowhite", "navy", "oldlace", "olive", "olivedrab", "orange",
            "orangered", "orchid", "palegoldenrod", "palegreen", "paleturquoise",
            "palevioletred", "papayawhip", "peachpuff", "peru", "pink", "plum",
            "powderblue", "purple", "rebeccapurple", "red", "rosybrown", "royalblue",
            "saddlebrown", "salmon", "sandybrown", "seagreen", "seashell", "sienna",
            "silver", "skyblue", "slateblue", "slategray", "slategrey", "snow",
            "springgreen", "steelblue", "tan", "teal", "thistle", "tomato", "turquoise",
            "violet", "wheat", "white", "whitesmoke", "yellow", "yellowgreen" };

        foreach (var name in names)
        {
            Color.TryParseNamed(name).Should().NotBeNull($"named color '{name}' should be recognized");
        }

        names.Length.Should().Be(148);
    }

    [Fact]
    public void TryParse_Rgb_Function()
    {
        var color = Color.TryParse("rgb(255, 0, 128)");
        color.Should().NotBeNull();
        color!.Value.R.Should().Be(255);
        color.Value.G.Should().Be(0);
        color.Value.B.Should().Be(128);
        color.Value.A.Should().Be(255);
    }

    [Fact]
    public void TryParse_Rgba_Function()
    {
        var color = Color.TryParse("rgba(100, 200, 50, 0.5)");
        color.Should().NotBeNull();
        color!.Value.R.Should().Be(100);
        color.Value.G.Should().Be(200);
        color.Value.B.Should().Be(50);
        color.Value.A.Should().BeInRange(126, 128); // 0.5 * 255 ≈ 128
    }

    [Fact]
    public void TryParse_Rgba_Percent()
    {
        var color = Color.TryParse("rgba(100%, 0%, 50%, 0.8)");
        color.Should().NotBeNull();
        color!.Value.R.Should().Be(255);
        color.Value.G.Should().Be(0);
        color.Value.B.Should().BeInRange(127, 128);
    }

    [Fact]
    public void TryParse_Hsl_Function()
    {
        // hsl(0, 100%, 50%) = pure red
        var color = Color.TryParse("hsl(0, 100%, 50%)");
        color.Should().NotBeNull();
        color!.Value.R.Should().Be(255);
        color.Value.G.Should().Be(0);
        color.Value.B.Should().Be(0);
    }

    [Fact]
    public void TryParse_Hsl_Green()
    {
        // hsl(120, 100%, 50%) = pure green
        var color = Color.TryParse("hsl(120, 100%, 50%)");
        color.Should().NotBeNull();
        color!.Value.R.Should().Be(0);
        color.Value.G.Should().Be(255);
        color.Value.B.Should().Be(0);
    }

    [Fact]
    public void TryParse_Hsl_Blue()
    {
        // hsl(240, 100%, 50%) = pure blue
        var color = Color.TryParse("hsl(240, 100%, 50%)");
        color.Should().NotBeNull();
        color!.Value.R.Should().Be(0);
        color.Value.G.Should().Be(0);
        color.Value.B.Should().Be(255);
    }

    [Fact]
    public void TryParse_Hsla_WithAlpha()
    {
        var color = Color.TryParse("hsla(0, 100%, 50%, 0.5)");
        color.Should().NotBeNull();
        color!.Value.R.Should().Be(255);
        color.Value.A.Should().BeInRange(126, 128);
    }

    [Fact]
    public void TryParse_ModernSyntax_Rgb()
    {
        // Modern CSS: rgb(255 0 128 / 0.5)
        var color = Color.TryParse("rgb(255 0 128 / 0.5)");
        color.Should().NotBeNull();
        color!.Value.R.Should().Be(255);
        color.Value.G.Should().Be(0);
        color.Value.B.Should().Be(128);
        color.Value.A.Should().BeInRange(126, 128);
    }

    [Fact]
    public void TryParse_Hex_WorksViaTryParse()
    {
        var color = Color.TryParse("#ff0000");
        color.Should().NotBeNull();
        color!.Value.R.Should().Be(255);
    }

    [Fact]
    public void TryParse_Named_WorksViaTryParse()
    {
        var color = Color.TryParse("red");
        color.Should().NotBeNull();
        color!.Value.R.Should().Be(255);
    }

    [Fact]
    public void TryParse_Transparent()
    {
        var color = Color.TryParse("transparent");
        color.Should().NotBeNull();
        color!.Value.A.Should().Be(0);
    }

    [Fact]
    public void TryParse_Invalid_ReturnsNull()
    {
        Color.TryParse("notacolor").Should().BeNull();
        Color.TryParse("").Should().BeNull();
        Color.TryParse(null).Should().BeNull();
    }

    [Fact]
    public void TryParse_Hsl_White()
    {
        // hsl(0, 0%, 100%) = white
        var color = Color.TryParse("hsl(0, 0%, 100%)");
        color.Should().NotBeNull();
        color!.Value.R.Should().Be(255);
        color.Value.G.Should().Be(255);
        color.Value.B.Should().Be(255);
    }

    [Fact]
    public void TryParse_Hsl_Black()
    {
        // hsl(0, 0%, 0%) = black
        var color = Color.TryParse("hsl(0, 0%, 0%)");
        color.Should().NotBeNull();
        color!.Value.R.Should().Be(0);
        color.Value.G.Should().Be(0);
        color.Value.B.Should().Be(0);
    }

    [Fact]
    public void TryParse_Hsl_NegativeHue()
    {
        // hsl(-120, 100%, 50%) should normalize to hsl(240, ...)  = blue
        var color = Color.TryParse("hsl(-120, 100%, 50%)");
        color.Should().NotBeNull();
        color!.Value.R.Should().Be(0);
        color.Value.G.Should().Be(0);
        color.Value.B.Should().Be(255);
    }

    [Fact]
    public void TryParse_Rgb_ZeroAlpha()
    {
        var color = Color.TryParse("rgba(255, 0, 0, 0)");
        color.Should().NotBeNull();
        color!.Value.R.Should().Be(255);
        color.Value.A.Should().Be(0);
        color.Value.IsTransparent.Should().BeTrue();
    }

    [Fact]
    public void TryParse_Rgb_Clamping()
    {
        // Values beyond 255 should clamp
        var color = Color.TryParse("rgb(300, -10, 128)");
        color.Should().NotBeNull();
        color!.Value.R.Should().Be(255); // clamped to max
        color.Value.G.Should().Be(0);    // clamped to min
        color.Value.B.Should().Be(128);
    }

    [Fact]
    public void TryParse_ModernSyntax_Hsl()
    {
        // Modern CSS: hsl(120 100% 50% / 0.8)
        var color = Color.TryParse("hsl(120 100% 50% / 0.8)");
        color.Should().NotBeNull();
        color!.Value.R.Should().Be(0);
        color.Value.G.Should().Be(255);
        color.Value.B.Should().Be(0);
        color.Value.A.Should().BeInRange(203, 205); // 0.8 * 255 ≈ 204
    }

    [Fact]
    public void TryParse_Hex_ShortForm()
    {
        var color = Color.TryParse("#f0a");
        color.Should().NotBeNull();
        color!.Value.R.Should().Be(255);
        color.Value.G.Should().Be(0);
        color.Value.B.Should().Be(170);
    }

    [Fact]
    public void TryParse_WhitespaceAroundValue()
    {
        var color = Color.TryParse("  rgb(255, 0, 0)  ");
        color.Should().NotBeNull();
        color!.Value.R.Should().Be(255);
    }

    // ── color-mix() ─────────────────────────────────────────────────────────

    [Fact]
    public void ColorMix_EqualParts_ProducesMiddleColor()
    {
        // color-mix(in srgb, red, blue) → 50% red + 50% blue → (128, 0, 128) ± 1
        var c = Color.TryParse("color-mix(in srgb, red, blue)");
        c.Should().NotBeNull();
        c!.Value.R.Should().BeInRange(127, 128);
        c.Value.G.Should().Be(0);
        c.Value.B.Should().BeInRange(127, 128);
    }

    [Fact]
    public void ColorMix_WithPercentages_RespectsWeights()
    {
        // color-mix(in srgb, red 100%, blue 0%) → pure red
        var c = Color.TryParse("color-mix(in srgb, red 100%, blue 0%)");
        c.Should().NotBeNull();
        c!.Value.R.Should().Be(255);
        c.Value.B.Should().Be(0);
    }

    [Fact]
    public void ColorMix_FirstColorHeavy_CloserToFirst()
    {
        // color-mix(in srgb, red 75%, blue 25%) → R=191, B=64
        var c = Color.TryParse("color-mix(in srgb, red 75%, blue 25%)");
        c.Should().NotBeNull();
        c!.Value.R.Should().BeInRange(190, 192);
        c.Value.B.Should().BeInRange(63, 65);
    }

    [Fact]
    public void ColorMix_WhiteAndBlack_ProducesGray()
    {
        // color-mix(in srgb, white, black) → (128, 128, 128)
        var c = Color.TryParse("color-mix(in srgb, white, black)");
        c.Should().NotBeNull();
        c!.Value.R.Should().BeInRange(127, 128);
        c.Value.G.Should().BeInRange(127, 128);
        c.Value.B.Should().BeInRange(127, 128);
    }

    [Fact]
    public void ColorMix_InvalidSyntax_ReturnsNull()
    {
        Color.TryParse("color-mix(in srgb, red)").Should().BeNull();
    }
}
