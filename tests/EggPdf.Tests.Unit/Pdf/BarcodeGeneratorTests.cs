using System.Text.RegularExpressions;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class BarcodeGeneratorTests
{
    [Fact]
    public void Code128_Hello_EncodesStartDataChecksumAndStop()
    {
        BarcodeGenerator.Code128Symbols("Hello").Should().Equal(104, 40, 69, 76, 76, 79, 76, 106);
    }

    [Fact]
    public void Code128_UpperCaseAndDigits_UseTheirOwnPatterns()
    {
        BarcodeGenerator.Code128Symbols("Z9").Should().Equal(104, 58, 25, (104 + 58 + 2 * 25) % 103, 106);
    }

    [Fact]
    public void Code128_Hello_DrawsThreeBarsPerSymbolAndFourForStop()
    {
        var content = BarcodeGenerator.GenerateCode128("Hello", 0, 0, 180, 40);

        Regex.Matches(content, " re\n").Count.Should().Be(7 * 3 + 4);
    }

    [Fact]
    public void Code128_NonAsciiCharacter_IsEncodedAsQuestionMark()
    {
        BarcodeGenerator.Code128Symbols("é")[1].Should().Be('?' - ' ');
    }

    [Fact]
    public void Ean13_NonDigitInput_ReturnsEmptyInsteadOfThrowing()
    {
        BarcodeGenerator.GenerateEAN13("ABCDEFGHIJKL", 0, 0, 100, 40).Should().BeEmpty();
    }

    [Fact]
    public void Ean13_ValidInput_DrawsBars()
    {
        BarcodeGenerator.GenerateEAN13("400638133393", 0, 0, 100, 40).Should().Contain("re f");
    }
}
