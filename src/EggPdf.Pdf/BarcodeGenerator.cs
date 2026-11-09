using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Generates Code 128 and EAN-13 barcodes as PDF vector paths.
/// Renders crisp at any zoom level (no rasterization).
/// </summary>
public static class BarcodeGenerator
{
    private const int StartB = 104;
    private const int Stop = 106;
    private const int ChecksumModulus = 103;
    private const char FirstCodeBCharacter = ' ';
    private const char LastCodeBCharacter = (char)127;
    private const int ModulesPerSymbol = 11;
    private const int StopModules = 13;

    private static readonly string[] Code128Patterns =
    {
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112"
    };

    /// <summary>
    /// Generate a Code 128 (code set B) barcode as PDF content stream commands filling a
    /// <paramref name="width"/> x <paramref name="height"/> box whose bottom-left corner is at
    /// (<paramref name="x"/>, <paramref name="y"/>). Characters outside printable ASCII are encoded as '?'.
    /// </summary>
    public static string GenerateCode128(string data, float x, float y, float width, float height)
    {
        if (string.IsNullOrEmpty(data)) return "";

        var symbols = Code128Symbols(data);
        int totalModules = (symbols.Count - 1) * ModulesPerSymbol + StopModules;
        float moduleWidth = width / totalModules;

        var sb = new StringBuilder();
        sb.AppendOpLine("q");
        sb.AppendOpLine("0 0 0 rg");
        float xPos = x;
        foreach (int symbol in symbols)
            xPos = DrawPattern(sb, Code128Patterns[symbol], xPos, y, moduleWidth, height);
        sb.AppendOpLine("f");
        sb.AppendOpLine("Q");
        return sb.ToString();
    }

    internal static List<int> Code128Symbols(string data)
    {
        var symbols = new List<int>(data.Length + 3) { StartB };
        int checksum = StartB;
        for (int i = 0; i < data.Length; i++)
        {
            char c = data[i];
            if (c < FirstCodeBCharacter || c > LastCodeBCharacter) c = '?';
            int value = c - FirstCodeBCharacter;
            symbols.Add(value);
            checksum += (i + 1) * value;
        }
        symbols.Add(checksum % ChecksumModulus);
        symbols.Add(Stop);
        return symbols;
    }

    /// <summary>
    /// Generate an EAN-13 barcode as PDF content stream commands. <paramref name="data"/> must start with 12
    /// digits; the check digit is always computed. Returns an empty string for anything else.
    /// </summary>
    public static string GenerateEAN13(string data, float x, float y, float width, float height)
    {
        if (string.IsNullOrEmpty(data) || data.Length < 12) return "";
        for (int i = 0; i < 12; i++)
            if (data[i] < '0' || data[i] > '9') return "";

        string digits = data.Substring(0, 12);
        digits += CalculateEAN13CheckDigit(digits).ToString(CultureInfo.InvariantCulture);

        string[][] lPatterns = {
            new[]{"0001101","0011001","0010011","0111101","0100011","0110001","0101111","0111011","0110111","0001011"},
            new[]{"0100111","0110011","0011011","0100001","0011101","0111001","0000101","0010001","0001001","0010111"}
        };
        string[] rPatterns = {"1110010","1100110","1101100","1000010","1011100","1001110","1010000","1000100","1001000","1110100"};
        int[][] parityPatterns = {
            new[]{0,0,0,0,0,0}, new[]{0,0,1,0,1,1}, new[]{0,0,1,1,0,1}, new[]{0,0,1,1,1,0},
            new[]{0,1,0,0,1,1}, new[]{0,1,1,0,0,1}, new[]{0,1,1,1,0,0}, new[]{0,1,0,1,0,1},
            new[]{0,1,0,1,1,0}, new[]{0,1,1,0,1,0}
        };

        var sb = new StringBuilder();
        sb.AppendOpLine("q");
        sb.AppendOpLine("0 0 0 rg");

        float moduleWidth = width / 95f;
        float xPos = x;

        xPos = DrawBar(sb, xPos, y, moduleWidth, height);
        xPos += moduleWidth;
        xPos = DrawBar(sb, xPos, y, moduleWidth, height);

        var parity = parityPatterns[digits[0] - '0'];
        for (int i = 0; i < 6; i++)
            xPos = DrawModules(sb, lPatterns[parity[i]][digits[i + 1] - '0'], xPos, y, moduleWidth, height);

        xPos += moduleWidth;
        xPos = DrawBar(sb, xPos, y, moduleWidth, height);
        xPos += moduleWidth;
        xPos = DrawBar(sb, xPos, y, moduleWidth, height);
        xPos += moduleWidth;

        for (int i = 0; i < 6; i++)
            xPos = DrawModules(sb, rPatterns[digits[i + 7] - '0'], xPos, y, moduleWidth, height);

        xPos = DrawBar(sb, xPos, y, moduleWidth, height);
        xPos += moduleWidth;
        DrawBar(sb, xPos, y, moduleWidth, height);

        sb.AppendOpLine("Q");
        return sb.ToString();
    }

    private static float DrawPattern(StringBuilder sb, string pattern, float x, float y, float moduleWidth, float height)
    {
        bool isBar = true;
        float xPos = x;
        foreach (char w in pattern)
        {
            float barWidth = (w - '0') * moduleWidth;
            if (isBar)
                sb.AppendOpLine($"{F(xPos)} {F(y)} {F(barWidth)} {F(height)} re");
            xPos += barWidth;
            isBar = !isBar;
        }
        return xPos;
    }

    private static float DrawModules(StringBuilder sb, string modules, float x, float y, float moduleWidth, float height)
    {
        float xPos = x;
        foreach (char c in modules)
        {
            if (c == '1')
                DrawBar(sb, xPos, y, moduleWidth, height);
            xPos += moduleWidth;
        }
        return xPos;
    }

    private static float DrawBar(StringBuilder sb, float x, float y, float moduleWidth, float height)
    {
        sb.AppendOpLine($"{F(x)} {F(y)} {F(moduleWidth)} {F(height)} re f");
        return x + moduleWidth;
    }

    private static int CalculateEAN13CheckDigit(string digits)
    {
        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            int d = digits[i] - '0';
            sum += (i % 2 == 0) ? d : d * 3;
        }
        return (10 - (sum % 10)) % 10;
    }

    private static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
}
