using System;
using System.Globalization;
using System.Text;

namespace EggPdf.Pdf;

internal static class PdfObjectSerializer
{
    public static string Serialize(PdfObject value)
    {
        var output = new StringBuilder();
        Write(output, value);
        return output.ToString();
    }

    public static string Number(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return "0";
        double rounded = Math.Round(value, 4);
        return rounded == Math.Floor(rounded) && Math.Abs(rounded) < 1e15
            ? ((long)rounded).ToString(CultureInfo.InvariantCulture)
            : rounded.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static void Write(StringBuilder output, PdfObject value)
    {
        switch (value)
        {
            case PdfNull:
                output.Append("null");
                break;
            case PdfBoolean boolean:
                output.Append(boolean.Value ? "true" : "false");
                break;
            case PdfNumber number:
                output.Append(Number(number.Value));
                break;
            case PdfName name:
                WriteName(output, name.Value);
                break;
            case PdfString text:
                output.Append('<');
                foreach (var b in text.Bytes) output.Append(b.ToString("X2", CultureInfo.InvariantCulture));
                output.Append('>');
                break;
            case PdfReference reference:
                output.Append(reference.ObjectNumber.ToString(CultureInfo.InvariantCulture)).Append(' ')
                      .Append(reference.Generation.ToString(CultureInfo.InvariantCulture)).Append(" R");
                break;
            case PdfArray array:
                output.Append('[');
                for (int i = 0; i < array.Items.Count; i++)
                {
                    if (i > 0) output.Append(' ');
                    Write(output, array.Items[i]);
                }
                output.Append(']');
                break;
            case PdfDictionary dictionary:
                output.Append("<<");
                foreach (var pair in dictionary.Entries)
                {
                    output.Append(' ');
                    WriteName(output, pair.Key);
                    output.Append(' ');
                    Write(output, pair.Value);
                }
                output.Append(" >>");
                break;
            default:
                throw new NotSupportedException("A stream can only be written as an indirect object.");
        }
    }

    private static void WriteName(StringBuilder output, string name)
    {
        output.Append('/');
        foreach (var b in Encoding.UTF8.GetBytes(name))
        {
            if (b < 0x21 || b > 0x7E || b == '#' || PdfSyntaxParser.IsDelimiter(b))
                output.Append('#').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            else
                output.Append((char)b);
        }
    }
}
