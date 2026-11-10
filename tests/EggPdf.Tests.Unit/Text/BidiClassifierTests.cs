using EggPdf.Text;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class BidiClassifierTests
{
    [Theory]
    [InlineData(0xFE50, BidiType.CS)]
    [InlineData(0xFE52, BidiType.CS)]
    [InlineData(0xFE55, BidiType.CS)]
    [InlineData(0xFE5F, BidiType.ET)]
    [InlineData(0xFE62, BidiType.ES)]
    [InlineData(0xFE63, BidiType.ES)]
    [InlineData(0xFE69, BidiType.ET)]
    [InlineData(0xFE6A, BidiType.ET)]
    [InlineData(0xFE51, BidiType.ON)]
    [InlineData(0x212E, BidiType.ET)]
    [InlineData(0x0890, BidiType.AN)]
    [InlineData(0x0891, BidiType.AN)]
    public void Classify_BmpCodePoint_MatchesTheUnicodeData(int codePoint, BidiType expected)
    {
        BidiClassifier.Classify((char)codePoint).Should().Be(expected);
    }

    [Theory]
    [InlineData(0x1EE00, BidiType.AL)]
    [InlineData(0x1EC71, BidiType.AL)]
    [InlineData(0x1ED01, BidiType.AL)]
    [InlineData(0x10EC2, BidiType.AL)]
    [InlineData(0x10F30, BidiType.AL)]
    [InlineData(0x10D30, BidiType.AN)]
    [InlineData(0x10D00, BidiType.AL)]
    [InlineData(0x1E900, BidiType.R)]
    [InlineData(0x10900, BidiType.R)]
    [InlineData(0x1F101, BidiType.EN)]
    public void ClassifySupplementary_CodePoint_MatchesTheUnicodeData(int codePoint, BidiType expected)
    {
        BidiClassifier.ClassifySupplementary(char.ConvertFromUtf32(codePoint), 0).Should().Be(expected);
    }

    [Fact]
    public void ContainsRTL_SupplementaryRightToLeftScript_IsDetected()
    {
        var adlam = char.ConvertFromUtf32(0x1E922) + char.ConvertFromUtf32(0x1E923);

        BidiAlgorithm.ContainsRTL("abc " + adlam).Should().BeTrue("Adlam is written right to left, so the line must be reordered");
        BidiAlgorithm.ContainsRTL("abc " + char.ConvertFromUtf32(0x1F600)).Should().BeFalse();
    }
}
