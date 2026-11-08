using System.Text;

namespace EggPdf.Css;

internal static class CssStringEscapes
{
    private const int MaxHexDigits = 6;
    private const int MaxCodepoint = 0x10FFFF;
    private const int FirstSurrogate = 0xD800;
    private const int LastSurrogate = 0xDFFF;
    private static readonly string ReplacementCharacterText = ((char)0xFFFD).ToString();

    internal static void AppendEscape(string input, ref int pos, StringBuilder output)
    {
        if (pos >= input.Length) return;
        char c = input[pos];
        if (c == '\n' || c == '\f')
        {
            pos++;
            return;
        }
        if (c == '\r')
        {
            pos++;
            if (pos < input.Length && input[pos] == '\n') pos++;
            return;
        }
        if (!IsHexDigit(c))
        {
            output.Append(c);
            pos++;
            return;
        }

        int codepoint = 0;
        int digits = 0;
        while (pos < input.Length && digits < MaxHexDigits && IsHexDigit(input[pos]))
        {
            codepoint = codepoint * 16 + HexValue(input[pos]);
            pos++;
            digits++;
        }
        if (pos < input.Length && IsCssWhitespace(input[pos]))
        {
            if (input[pos] == '\r' && pos + 1 < input.Length && input[pos + 1] == '\n') pos++;
            pos++;
        }

        if (codepoint == 0 || codepoint > MaxCodepoint || (codepoint >= FirstSurrogate && codepoint <= LastSurrogate))
            output.Append(ReplacementCharacterText);
        else
            output.Append(char.ConvertFromUtf32(codepoint));
    }

    internal static string ReadQuoted(string input, ref int pos)
    {
        char quote = input[pos];
        pos++;
        var sb = new StringBuilder();
        while (pos < input.Length)
        {
            char c = input[pos];
            if (c == quote) { pos++; break; }
            if (c == '\\')
            {
                pos++;
                AppendEscape(input, ref pos, sb);
                continue;
            }
            sb.Append(c);
            pos++;
        }
        return sb.ToString();
    }

    internal static string Quote(string value)
    {
        bool hasSingle = value.IndexOf('\'') >= 0;
        bool hasDouble = value.IndexOf('"') >= 0;
        bool hasBackslash = value.IndexOf('\\') >= 0;
        if (!hasSingle && !hasBackslash) return "'" + value + "'";
        if (!hasDouble && !hasBackslash) return "\"" + value + "\"";

        var sb = new StringBuilder(value.Length + 4);
        sb.Append('\'');
        foreach (char c in value)
        {
            if (c == '\'' || c == '\\') sb.Append('\\');
            sb.Append(c);
        }
        sb.Append('\'');
        return sb.ToString();
    }

    internal static int SkipEscape(string input, int backslash)
    {
        int pos = backslash + 1;
        if (pos >= input.Length) return input.Length;
        if (!IsHexDigit(input[pos])) return pos + 1;

        int digits = 0;
        while (pos < input.Length && digits < MaxHexDigits && IsHexDigit(input[pos]))
        {
            pos++;
            digits++;
        }
        if (pos < input.Length && IsCssWhitespace(input[pos]))
        {
            if (input[pos] == '\r' && pos + 1 < input.Length && input[pos + 1] == '\n') pos++;
            pos++;
        }
        return pos;
    }

    internal static void AppendIdentifier(StringBuilder output, string identifier)
    {
        if (identifier.Length == 1 && identifier[0] == '-')
        {
            output.Append("\\-");
            return;
        }

        for (int i = 0; i < identifier.Length; i++)
        {
            char c = identifier[i];
            bool leadingDigit = c >= '0' && c <= '9' && (i == 0 || (i == 1 && identifier[0] == '-'));
            if (c == '\0')
                output.Append(ReplacementCharacterText);
            else if (c < 0x20 || c == 0x7F || leadingDigit)
                output.Append('\\').Append(((int)c).ToString("x", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
            else if (c >= 0x80 || c == '-' || c == '_' || (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                output.Append(c);
            else
                output.Append('\\').Append(c);
        }
    }

    private static bool IsHexDigit(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    private static bool IsCssWhitespace(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f';
}
