using System.Text;

namespace EggPdf.Pdf;

internal static class PdfTextStrings
{
    private const char FirstPrintableAscii = ' ';
    private const char LastPrintableAscii = '~';
    private static readonly byte[] Utf16BigEndianByteOrderMark = { 0xFE, 0xFF };

    internal static bool IsPrintableAscii(string text)
    {
        for (int i = 0; i < text.Length; i++)
            if (text[i] < FirstPrintableAscii || text[i] > LastPrintableAscii) return false;
        return true;
    }

    internal static string Encode(string text) =>
        IsPrintableAscii(text) ? "(" + EscapeLiteral(text) + ")" : Hex(TextStringBytes(text));

    internal static byte[] TextStringBytes(string text)
    {
        if (IsPrintableAscii(text)) return Encoding.ASCII.GetBytes(text);
        var utf16 = Encoding.BigEndianUnicode.GetBytes(text);
        var bytes = new byte[utf16.Length + Utf16BigEndianByteOrderMark.Length];
        Utf16BigEndianByteOrderMark.CopyTo(bytes, 0);
        utf16.CopyTo(bytes, Utf16BigEndianByteOrderMark.Length);
        return bytes;
    }

    internal static string EscapeLiteral(string text)
        => text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    internal static string Hex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2 + 2);
        sb.Append('<');
        for (int i = 0; i < bytes.Length; i++)
            sb.Append(HexDigit(bytes[i] >> 4)).Append(HexDigit(bytes[i] & 0xF));
        sb.Append('>');
        return sb.ToString();
    }

    internal static string UriToAscii(string uri)
    {
        if (IsPrintableAscii(uri) && uri.IndexOf(' ') < 0) return uri;

        var sb = new StringBuilder(uri.Length + 16);
        foreach (byte b in Encoding.UTF8.GetBytes(uri))
        {
            if (b > FirstPrintableAscii && b <= LastPrintableAscii)
                sb.Append((char)b);
            else
                sb.Append('%').Append(HexDigit(b >> 4)).Append(HexDigit(b & 0xF));
        }
        return sb.ToString();
    }

    private static char HexDigit(int value) => (char)(value < 10 ? '0' + value : 'A' + value - 10);
}
