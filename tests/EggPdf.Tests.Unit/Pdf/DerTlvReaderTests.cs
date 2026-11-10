using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class DerTlvReaderTests
{
    private const byte Sequence = 0x30;
    private const byte LongFormOneLengthByte = 0x81, LongFormTwoLengthBytes = 0x82;

    [Fact]
    public void ReadTotalLength_ShortForm_ReturnsTagPlusLengthPlusContent()
    {
        const byte shortFormLength3 = 0x03;
        var data = new byte[] { Sequence, shortFormLength3, 0xAA, 0xBB, 0xCC, 0x00, 0x00 };

        DerTlvReader.ReadTotalLength(data).Should().Be(5);
    }

    [Fact]
    public void ReadTotalLength_LongForm1Byte_ReturnsTagPlusLengthPlusContent()
    {
        var content = new byte[128];
        var data = new byte[3 + content.Length + 2];
        data[0] = Sequence;
        data[1] = LongFormOneLengthByte;
        data[2] = 0x80;

        DerTlvReader.ReadTotalLength(data).Should().Be(3 + 128);
    }

    [Fact]
    public void ReadTotalLength_LongForm2Byte_ReturnsTagPlusLengthPlusContent()
    {
        var data = new byte[4 + 256 + 4];
        data[0] = Sequence;
        data[1] = LongFormTwoLengthBytes;
        data[2] = 0x01;
        data[3] = 0x00;

        DerTlvReader.ReadTotalLength(data).Should().Be(4 + 256);
    }

    [Fact]
    public void ReadTotalLength_ContentEndsInZeroByte_StillReturnsExactLength()
    {
        var data = new byte[] { Sequence, 0x04, 0x01, 0x02, 0x03, 0x00, 0x00, 0x00 };

        DerTlvReader.ReadTotalLength(data).Should().Be(6,
            "the trailing 0x00 at index 5 is real content, not padding; trimming trailing '0' hex digits from the " +
            "signature's /Contents ate such bytes and broke the CMS about 1 run in 256, while the DER length header can't");
    }

    [Fact]
    public void ReadTotalLength_ContentEndsInMultipleZeroBytes_StillReturnsExactLength()
    {
        var data = new byte[] { Sequence, 0x05, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 };

        DerTlvReader.ReadTotalLength(data).Should().Be(7,
            "three trailing 0x00 content bytes must all be counted, distinguishing them from padding");
    }
}
