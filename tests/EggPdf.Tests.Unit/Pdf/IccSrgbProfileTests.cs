using System;
using System.Text;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class IccSrgbProfileTests
{
    private static int U32(byte[] b, int at) => (b[at] << 24) | (b[at + 1] << 16) | (b[at + 2] << 8) | b[at + 3];

    private static int U16(byte[] b, int at) => (b[at] << 8) | b[at + 1];

    private static int TagOffset(byte[] profile, string signature)
    {
        int count = U32(profile, 128);
        for (int i = 0; i < count; i++)
        {
            int entry = 132 + i * 12;
            if (Encoding.ASCII.GetString(profile, entry, 4) == signature) return U32(profile, entry + 4);
        }
        throw new InvalidOperationException(signature + " missing");
    }

    [Fact]
    public void ToneCurve_SamplesTheSrgbTransferFunction()
    {
        var profile = IccSrgbProfile.Generate();
        int curve = TagOffset(profile, "rTRC");
        int entries = U32(profile, curve + 8);

        entries.Should().BeGreaterThan(1, "a single entry would be a plain gamma, not sRGB's piecewise curve");
        double LinearAt(double encoded) => U16(profile, curve + 12 + 2 * (int)Math.Round(encoded * (entries - 1))) / 65535.0;
        LinearAt(0.5).Should().BeApproximately(0.2140, 0.002, "sRGB 50% grey is 21.4% linear light (gamma 2.2 would give 21.8%)");
        LinearAt(0.02).Should().BeApproximately(0.02 / 12.92, 0.0005, "near black the sRGB curve is linear");
        LinearAt(1.0).Should().BeApproximately(1.0, 1e-6);
    }

    [Fact]
    public void Profile_SizeFieldMatchesItsLength()
    {
        var profile = IccSrgbProfile.Generate();

        U32(profile, 0).Should().Be(profile.Length);
        Encoding.ASCII.GetString(profile, 36, 4).Should().Be("acsp");
    }
}
