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
}
