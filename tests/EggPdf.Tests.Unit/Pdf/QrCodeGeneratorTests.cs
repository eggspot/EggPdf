using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class QrCodeGeneratorTests
{
    private static readonly int[] DataCodewordsM = { 0, 16, 28, 44, 64, 86, 108, 124, 154, 182, 216 };
    private static readonly int[] EcPerBlockM = { 0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
    private static readonly int[][] BlocksM =
    {
        new int[0], new[] { 16 }, new[] { 28 }, new[] { 44 }, new[] { 32, 32 }, new[] { 43, 43 },
        new[] { 27, 27, 27, 27 }, new[] { 31, 31, 31, 31 }, new[] { 38, 38, 39, 39 }, new[] { 36, 36, 36, 37, 37 },
        new[] { 43, 43, 43, 43, 44 }
    };
    private static readonly int[][] AlignmentCentres =
    {
        new int[0], new int[0], new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 }, new[] { 6, 30 }, new[] { 6, 34 },
        new[] { 6, 22, 38 }, new[] { 6, 24, 42 }, new[] { 6, 26, 46 }, new[] { 6, 28, 50 }
    };

    [Fact]
    public void ReedSolomon_HelloWorldVersion1M_MatchesThePublishedCodewords()
    {
        byte[] data = { 32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236, 17, 236, 17 };

        QrCodeGenerator.ReedSolomonRemainder(data, 10).Should().Equal(196, 35, 39, 119, 235, 215, 231, 226, 93, 23);
    }

    [Theory]
    [InlineData(0, "101010000010010")]
    [InlineData(3, "101101101001011")]
    [InlineData(5, "100000011001110")]
    [InlineData(7, "100101010100000")]
    public void FormatBits_LevelM_MatchTheSpecTable(int mask, string expected)
    {
        Convert.ToString(QrCodeGenerator.FormatBits(mask), 2).PadLeft(15, '0').Should().Be(expected);
    }

    [Theory]
    [InlineData("HELLO WORLD")]
    [InlineData("https://example.com/invoice/2026/000123?pay=now")]
    [InlineData("Đơn hàng số 42 — thanh toán")]
    public void EncodedSymbol_ReadsBackToTheInput(string text)
    {
        var modules = QrCodeGenerator.EncodeToModules(text)!;

        ReadSymbol(modules).Should().Be(text);
    }

    [Fact]
    public void EncodedSymbol_LongPayload_UsesVersionSevenWithVersionInfo()
    {
        var text = new string('x', 120);
        var modules = QrCodeGenerator.EncodeToModules(text)!;

        modules.GetLength(0).Should().Be(45);
        ReadSymbol(modules).Should().Be(text);
    }

    [Fact]
    public void Generate_PayloadBeyondVersionTen_ReturnsEmpty()
    {
        QrCodeGenerator.Generate(new string('x', 300), 0, 0, 100).Should().BeEmpty();
    }

    [Fact]
    public void Generate_DrawsDarkModulesAsFilledRectangles()
    {
        var content = QrCodeGenerator.Generate("HELLO", 10, 20, 105);

        content.Should().Contain(" re").And.Contain("\nf\n");
    }

    private static string ReadSymbol(bool[,] modules)
    {
        int size = modules.GetLength(0);
        int version = (size - 17) / 4;
        int format = 0;
        for (int i = 0; i <= 5; i++) format |= (modules[i, 8] ? 1 : 0) << i;
        format |= (modules[7, 8] ? 1 : 0) << 6;
        format |= (modules[8, 8] ? 1 : 0) << 7;
        format |= (modules[8, 7] ? 1 : 0) << 8;
        for (int i = 9; i < 15; i++) format |= (modules[8, 14 - i] ? 1 : 0) << i;
        int unmasked = format ^ 0x5412;
        ((unmasked >> 13) & 3).Should().Be(0, "error correction level M");
        int mask = (unmasked >> 10) & 7;

        var reserved = Reserved(size, version);
        var bits = new List<bool>();
        bool upward = true;
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (int step = 0; step < size; step++)
            {
                int row = upward ? size - 1 - step : step;
                for (int c = 0; c < 2; c++)
                {
                    int col = right - c;
                    if (reserved[row, col]) continue;
                    bool bit = modules[row, col] ^ MaskBit(mask, row, col);
                    bits.Add(bit);
                }
            }
            upward = !upward;
        }

        int total = DataCodewordsM[version] + EcPerBlockM[version] * BlocksM[version].Length;
        var codewords = new byte[total];
        for (int i = 0; i < total; i++)
            for (int b = 0; b < 8; b++)
                codewords[i] = (byte)((codewords[i] << 1) | (bits[i * 8 + b] ? 1 : 0));

        var blocks = BlocksM[version];
        var dataBlocks = blocks.Select(n => new List<byte>()).ToArray();
        int k = 0;
        for (int i = 0; i < blocks.Max(); i++)
            for (int b = 0; b < blocks.Length; b++)
                if (i < blocks[b]) dataBlocks[b].Add(codewords[k++]);
        for (int b = 0; b < blocks.Length; b++)
        {
            var ec = new List<byte>();
            for (int i = 0; i < EcPerBlockM[version]; i++) ec.Add(codewords[k + i * blocks.Length + b]);
            QrCodeGenerator.ReedSolomonRemainder(dataBlocks[b].ToArray(), EcPerBlockM[version]).Should().Equal(ec);
        }

        var data = dataBlocks.SelectMany(d => d).ToArray();
        int pos = 0;
        int Read(int count)
        {
            int value = 0;
            for (int i = 0; i < count; i++, pos++)
                value = (value << 1) | ((data[pos / 8] >> (7 - pos % 8)) & 1);
            return value;
        }
        Read(4).Should().Be(4, "byte mode");
        int length = Read(version < 10 ? 8 : 16);
        var bytes = new byte[length];
        for (int i = 0; i < length; i++) bytes[i] = (byte)Read(8);
        return Encoding.UTF8.GetString(bytes);
    }

    private static bool MaskBit(int mask, int row, int col) => mask switch
    {
        0 => (row + col) % 2 == 0,
        1 => row % 2 == 0,
        2 => col % 3 == 0,
        3 => (row + col) % 3 == 0,
        4 => (row / 2 + col / 3) % 2 == 0,
        5 => row * col % 2 + row * col % 3 == 0,
        6 => (row * col % 2 + row * col % 3) % 2 == 0,
        _ => ((row + col) % 2 + row * col % 3) % 2 == 0,
    };

    private static bool[,] Reserved(int size, int version)
    {
        var reserved = new bool[size, size];
        void Block(int top, int left, int height, int width)
        {
            for (int r = top; r < top + height; r++)
                for (int c = left; c < left + width; c++)
                    if (r >= 0 && c >= 0 && r < size && c < size) reserved[r, c] = true;
        }
        Block(0, 0, 9, 9);
        Block(0, size - 8, 9, 8);
        Block(size - 8, 0, 8, 9);
        for (int i = 0; i < size; i++) { reserved[6, i] = true; reserved[i, 6] = true; }
        var centres = AlignmentCentres[version];
        foreach (int r in centres)
            foreach (int c in centres)
            {
                if ((r == 6 && c == 6) || (r == 6 && c == centres[centres.Length - 1]) || (r == centres[centres.Length - 1] && c == 6))
                    continue;
                Block(r - 2, c - 2, 5, 5);
            }
        if (version >= 7)
        {
            Block(0, size - 11, 6, 3);
            Block(size - 11, 0, 3, 6);
        }
        return reserved;
    }
}
