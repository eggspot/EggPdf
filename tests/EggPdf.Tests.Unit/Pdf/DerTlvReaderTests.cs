using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>
/// Pins the exact bug that made Sign_ProducesVerifiableDetachedCms flaky: the
/// old extraction used TrimEnd('0') on the hex-encoded /Contents field, which
/// silently eats a genuine trailing 0x00 signature byte along with the real
/// zero padding, corrupting the CMS blob roughly 1 in 256 runs (or worse,
/// when trimming an odd number of hex nibbles). Reading the DER length header
/// instead is immune to what the content bytes happen to be.
/// </summary>
public class DerTlvReaderTests
{
    [Fact]
    public void ReadTotalLength_ShortForm_ReturnsTagPlusLengthPlusContent()
    {
        // 30 03 AA BB CC = SEQUENCE, length 3, content AA BB CC
        var data = new byte[] { 0x30, 0x03, 0xAA, 0xBB, 0xCC, 0x00, 0x00 };

        DerTlvReader.ReadTotalLength(data).Should().Be(5);
    }

    [Fact]
    public void ReadTotalLength_LongForm1Byte_ReturnsTagPlusLengthPlusContent()
    {
        // 30 81 80 = SEQUENCE, 1 length-of-length byte, content length 0x80 (128)
        var content = new byte[128];
        var data = new byte[3 + content.Length + 2];
        data[0] = 0x30;
        data[1] = 0x81;
        data[2] = 0x80;

        DerTlvReader.ReadTotalLength(data).Should().Be(3 + 128);
    }

    [Fact]
    public void ReadTotalLength_LongForm2Byte_ReturnsTagPlusLengthPlusContent()
    {
        // 30 82 01 00 = SEQUENCE, 2 length-of-length bytes, content length 0x0100 (256)
        var data = new byte[4 + 256 + 4];
        data[0] = 0x30;
        data[1] = 0x82;
        data[2] = 0x01;
        data[3] = 0x00;

        DerTlvReader.ReadTotalLength(data).Should().Be(4 + 256);
    }

    [Fact]
    public void ReadTotalLength_ContentEndsInZeroByte_StillReturnsExactLength()
    {
        // The regression case: a genuine 0x00 as the very last content byte.
        // TrimEnd('0') on the hex string would have eaten this byte (and the
        // zero padding after it), reporting a length one byte short.
        var data = new byte[] { 0x30, 0x04, 0x01, 0x02, 0x03, 0x00, 0x00, 0x00 };

        DerTlvReader.ReadTotalLength(data).Should().Be(6,
            "the trailing 0x00 at index 5 is real content, not padding, and must be included");
    }

    [Fact]
    public void ReadTotalLength_ContentEndsInMultipleZeroBytes_StillReturnsExactLength()
    {
        var data = new byte[] { 0x30, 0x05, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00 };

        DerTlvReader.ReadTotalLength(data).Should().Be(7,
            "three trailing 0x00 content bytes must all be counted, distinguishing them from padding");
    }
}
