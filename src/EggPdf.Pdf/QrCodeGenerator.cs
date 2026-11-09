using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Generates QR codes (Model 2, byte mode, error correction level M, versions 1-10, up to 213 UTF-8 bytes)
/// as PDF vector paths. The quiet zone around the symbol is left to the caller.
/// </summary>
public static class QrCodeGenerator
{
    private const int MaxVersion = 10;
    private const int FormatMask = 0x5412;
    private const int FormatGenerator = 0x537;
    private const int VersionGenerator = 0x1F25;
    private const int PenaltyRun = 3;
    private const int PenaltyBlock = 3;
    private const int PenaltyFinderLike = 40;
    private const int PenaltyBalance = 10;

    private static readonly int[] DataCodewords = { 0, 16, 28, 44, 64, 86, 108, 124, 154, 182, 216 };
    private static readonly int[] EcCodewordsPerBlock = { 0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
    private static readonly int[] BlockCount = { 0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 };
    private static readonly int[][] AlignmentCentres =
    {
        new int[0], new int[0], new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 }, new[] { 6, 30 }, new[] { 6, 34 },
        new[] { 6, 22, 38 }, new[] { 6, 24, 42 }, new[] { 6, 26, 46 }, new[] { 6, 28, 50 }
    };

    /// <summary>
    /// PDF content stream commands that draw the QR code for <paramref name="data"/> as filled squares in a
    /// <paramref name="size"/>-point square whose bottom-left corner is at (<paramref name="x"/>, <paramref name="y"/>).
    /// Returns an empty string when <paramref name="data"/> is empty or longer than version 10 holds.
    /// </summary>
    public static string Generate(string data, float x, float y, float size)
    {
        if (string.IsNullOrEmpty(data)) return "";

        var modules = EncodeToModules(data);
        if (modules == null) return "";

        int moduleCount = modules.GetLength(0);
        float moduleSize = size / moduleCount;

        var sb = new StringBuilder();
        sb.AppendOpLine("q");
        sb.AppendOpLine("0 0 0 rg");
        for (int row = 0; row < moduleCount; row++)
        {
            for (int col = 0; col < moduleCount; col++)
            {
                if (!modules[row, col]) continue;
                float mx = x + col * moduleSize;
                float my = y + (moduleCount - 1 - row) * moduleSize;
                sb.AppendOpLine($"{F(mx)} {F(my)} {F(moduleSize)} {F(moduleSize)} re");
            }
        }
        sb.AppendOpLine("f");
        sb.AppendOpLine("Q");
        return sb.ToString();
    }

    internal static bool[,]? EncodeToModules(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        int version = SmallestVersionFor(bytes.Length);
        if (version == 0) return null;

        var codewords = AddErrorCorrection(DataCodewordsFor(bytes, version), version);
        int size = version * 4 + 17;
        var modules = new bool[size, size];
        var reserved = new bool[size, size];
        DrawFunctionPatterns(modules, reserved, version);
        PlaceCodewords(modules, reserved, codewords);

        int bestMask = 0, bestPenalty = int.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            ApplyMask(modules, reserved, mask);
            DrawFormatBits(modules, mask);
            int penalty = Penalty(modules);
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                bestMask = mask;
            }
            ApplyMask(modules, reserved, mask);
        }
        ApplyMask(modules, reserved, bestMask);
        DrawFormatBits(modules, bestMask);
        return modules;
    }

    internal static int FormatBits(int mask)
    {
        int data = mask;
        int remainder = data;
        for (int i = 0; i < 10; i++)
            remainder = (remainder << 1) ^ ((remainder >> 9) * FormatGenerator);
        return ((data << 10) | remainder) ^ FormatMask;
    }

    internal static byte[] ReedSolomonRemainder(byte[] data, int ecCount)
    {
        var generator = GeneratorPolynomial(ecCount);
        var remainder = new byte[ecCount];
        foreach (byte b in data)
        {
            byte factor = (byte)(b ^ remainder[0]);
            Array.Copy(remainder, 1, remainder, 0, ecCount - 1);
            remainder[ecCount - 1] = 0;
            for (int i = 0; i < ecCount; i++)
                remainder[i] ^= Multiply(generator[i], factor);
        }
        return remainder;
    }

    private static int SmallestVersionFor(int byteCount)
    {
        for (int version = 1; version <= MaxVersion; version++)
        {
            int countBits = version < 10 ? 8 : 16;
            if (4 + countBits + byteCount * 8 <= DataCodewords[version] * 8) return version;
        }
        return 0;
    }

    private static byte[] DataCodewordsFor(byte[] bytes, int version)
    {
        var bits = new List<bool>(DataCodewords[version] * 8);
        void Append(int value, int count)
        {
            for (int i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) == 1);
        }

        Append(0b0100, 4);
        Append(bytes.Length, version < 10 ? 8 : 16);
        foreach (byte b in bytes) Append(b, 8);

        int capacity = DataCodewords[version] * 8;
        Append(0, Math.Min(4, capacity - bits.Count));
        while (bits.Count % 8 != 0) bits.Add(false);
        for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) Append(pad, 8);

        var result = new byte[DataCodewords[version]];
        for (int i = 0; i < result.Length; i++)
            for (int b = 0; b < 8; b++)
                if (bits[i * 8 + b]) result[i] |= (byte)(0x80 >> b);
        return result;
    }

    private static byte[] AddErrorCorrection(byte[] data, int version)
    {
        int blocks = BlockCount[version];
        int ecCount = EcCodewordsPerBlock[version];
        int shortBlockLength = DataCodewords[version] / blocks;
        int longBlocks = DataCodewords[version] % blocks;
        int shortBlocks = blocks - longBlocks;

        var dataBlocks = new byte[blocks][];
        var ecBlocks = new byte[blocks][];
        int offset = 0;
        for (int b = 0; b < blocks; b++)
        {
            int length = shortBlockLength + (b < shortBlocks ? 0 : 1);
            dataBlocks[b] = new byte[length];
            Array.Copy(data, offset, dataBlocks[b], 0, length);
            offset += length;
            ecBlocks[b] = ReedSolomonRemainder(dataBlocks[b], ecCount);
        }

        var result = new List<byte>(data.Length + blocks * ecCount);
        for (int i = 0; i <= shortBlockLength; i++)
            for (int b = 0; b < blocks; b++)
                if (i < dataBlocks[b].Length) result.Add(dataBlocks[b][i]);
        for (int i = 0; i < ecCount; i++)
            for (int b = 0; b < blocks; b++)
                result.Add(ecBlocks[b][i]);
        return result.ToArray();
    }

    private static byte[] GeneratorPolynomial(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        byte root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < degree; j++)
            {
                result[j] = Multiply(result[j], root);
                if (j + 1 < degree) result[j] ^= result[j + 1];
            }
            root = Multiply(root, 0x02);
        }
        return result;
    }

    private static byte Multiply(byte x, byte y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }

    private static void DrawFunctionPatterns(bool[,] modules, bool[,] reserved, int version)
    {
        int size = modules.GetLength(0);
        for (int i = 0; i < size; i++)
        {
            SetFunction(modules, reserved, 6, i, i % 2 == 0);
            SetFunction(modules, reserved, i, 6, i % 2 == 0);
        }

        DrawFinder(modules, reserved, 3, 3);
        DrawFinder(modules, reserved, 3, size - 4);
        DrawFinder(modules, reserved, size - 4, 3);

        var centres = AlignmentCentres[version];
        int last = centres.Length - 1;
        for (int i = 0; i < centres.Length; i++)
            for (int j = 0; j < centres.Length; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == last) || (i == last && j == 0)) continue;
                for (int dr = -2; dr <= 2; dr++)
                    for (int dc = -2; dc <= 2; dc++)
                        SetFunction(modules, reserved, centres[i] + dr, centres[j] + dc,
                            Math.Max(Math.Abs(dr), Math.Abs(dc)) != 1);
            }

        DrawFormatBits(modules, 0);
        for (int i = 0; i < 9; i++)
        {
            reserved[8, i] = true;
            reserved[i, 8] = true;
        }
        for (int i = 0; i < 8; i++)
        {
            reserved[8, size - 1 - i] = true;
            reserved[size - 1 - i, 8] = true;
        }
        SetFunction(modules, reserved, size - 8, 8, true);

        if (version >= 7)
        {
            int remainder = version;
            for (int i = 0; i < 12; i++)
                remainder = (remainder << 1) ^ ((remainder >> 11) * VersionGenerator);
            int bits = (version << 12) | remainder;
            for (int i = 0; i < 18; i++)
            {
                bool bit = ((bits >> i) & 1) == 1;
                int a = size - 11 + i % 3, b = i / 3;
                SetFunction(modules, reserved, b, a, bit);
                SetFunction(modules, reserved, a, b, bit);
            }
        }
    }

    private static void DrawFinder(bool[,] modules, bool[,] reserved, int centreRow, int centreCol)
    {
        int size = modules.GetLength(0);
        for (int dr = -4; dr <= 4; dr++)
            for (int dc = -4; dc <= 4; dc++)
            {
                int row = centreRow + dr, col = centreCol + dc;
                if (row < 0 || col < 0 || row >= size || col >= size) continue;
                int distance = Math.Max(Math.Abs(dr), Math.Abs(dc));
                SetFunction(modules, reserved, row, col, distance != 2 && distance != 4);
            }
    }

    private static void DrawFormatBits(bool[,] modules, int mask)
    {
        int size = modules.GetLength(0);
        int bits = FormatBits(mask);
        bool Bit(int i) => ((bits >> i) & 1) == 1;

        for (int i = 0; i <= 5; i++) modules[i, 8] = Bit(i);
        modules[7, 8] = Bit(6);
        modules[8, 8] = Bit(7);
        modules[8, 7] = Bit(8);
        for (int i = 9; i < 15; i++) modules[8, 14 - i] = Bit(i);

        for (int i = 0; i < 8; i++) modules[8, size - 1 - i] = Bit(i);
        for (int i = 8; i < 15; i++) modules[size - 15 + i, 8] = Bit(i);
        modules[size - 8, 8] = true;
    }

    private static void SetFunction(bool[,] modules, bool[,] reserved, int row, int col, bool dark)
    {
        modules[row, col] = dark;
        reserved[row, col] = true;
    }

    private static void PlaceCodewords(bool[,] modules, bool[,] reserved, byte[] codewords)
    {
        int size = modules.GetLength(0);
        int bitIndex = 0;
        int totalBits = codewords.Length * 8;
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
                    if (bitIndex < totalBits)
                    {
                        modules[row, col] = ((codewords[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) == 1;
                        bitIndex++;
                    }
                }
            }
            upward = !upward;
        }
    }

    private static void ApplyMask(bool[,] modules, bool[,] reserved, int mask)
    {
        int size = modules.GetLength(0);
        for (int row = 0; row < size; row++)
            for (int col = 0; col < size; col++)
                if (!reserved[row, col] && MaskBit(mask, row, col))
                    modules[row, col] = !modules[row, col];
    }

    private static bool MaskBit(int mask, int row, int col)
    {
        switch (mask)
        {
            case 0: return (row + col) % 2 == 0;
            case 1: return row % 2 == 0;
            case 2: return col % 3 == 0;
            case 3: return (row + col) % 3 == 0;
            case 4: return (row / 2 + col / 3) % 2 == 0;
            case 5: return row * col % 2 + row * col % 3 == 0;
            case 6: return (row * col % 2 + row * col % 3) % 2 == 0;
            default: return ((row + col) % 2 + row * col % 3) % 2 == 0;
        }
    }

    private static int Penalty(bool[,] modules)
    {
        int size = modules.GetLength(0);
        int penalty = 0;
        int dark = 0;
        for (int a = 0; a < size; a++)
        {
            penalty += LinePenalty(modules, a, true) + LinePenalty(modules, a, false);
            for (int b = 0; b < size; b++)
            {
                if (modules[a, b]) dark++;
                if (a + 1 < size && b + 1 < size &&
                    modules[a, b] == modules[a + 1, b] && modules[a, b] == modules[a, b + 1] && modules[a, b] == modules[a + 1, b + 1])
                    penalty += PenaltyBlock;
            }
        }
        int total = size * size;
        int deviation = Math.Abs(dark * 20 - total * 10);
        penalty += ((deviation + total - 1) / total - 1) * PenaltyBalance;
        return penalty;
    }

    private static int LinePenalty(bool[,] modules, int index, bool horizontal)
    {
        int size = modules.GetLength(0);
        bool At(int i) => horizontal ? modules[index, i] : modules[i, index];
        int penalty = 0;
        int run = 1;
        for (int i = 1; i <= size; i++)
        {
            if (i < size && At(i) == At(i - 1))
            {
                run++;
                continue;
            }
            if (run >= 5) penalty += PenaltyRun + run - 5;
            run = 1;
        }
        for (int i = 0; i + 11 <= size; i++)
        {
            if (MatchesFinderLike(At, i, true) || MatchesFinderLike(At, i, false))
                penalty += PenaltyFinderLike;
        }
        return penalty;
    }

    private static readonly bool[] FinderLikeAfterLight = { false, false, false, false, true, false, true, true, true, false, true };
    private static readonly bool[] FinderLikeBeforeLight = { true, false, true, true, true, false, true, false, false, false, false };

    private static bool MatchesFinderLike(Func<int, bool> at, int start, bool lightBefore)
    {
        bool[] pattern = lightBefore ? FinderLikeAfterLight : FinderLikeBeforeLight;
        for (int i = 0; i < pattern.Length; i++)
            if (at(start + i) != pattern[i]) return false;
        return true;
    }

    private static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
}
