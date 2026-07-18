using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace EggPdf.Tests.E2E;

/// <summary>
/// Decodes a rendered PDF to assertable text: Latin-1 for the object
/// structure, with every FlateDecode stream inflated in place so tests can
/// assert on content-stream operators regardless of compression (page content
/// streams are FlateDecode-compressed by default since v1.4.3).
/// </summary>
public static class PdfTextDecoder
{
    /// <summary>
    /// Decode for assertions, appending a rendered-text layer.
    ///
    /// The engine positions each word as its own BT/ET block
    /// ("(Strong) Tj" then "( text) Tj"), so a phrase never appears
    /// contiguously in the raw operators. Substring-matching the stream would
    /// therefore test an implementation detail rather than the requirement
    /// ("this text renders"). The appended layer concatenates the shown
    /// strings so phrase assertions hold regardless of how runs are split,
    /// while structural assertions (/MediaBox, /Type /Page …) still work
    /// against the operator text above it.
    /// </summary>
    public static string DecodeWithText(byte[] pdf)
    {
        var decoded = Decode(pdf);
        return decoded + "\n% rendered-text-layer\n" + ExtractText(decoded);
    }

    /// <summary>
    /// Concatenate the strings shown by text operators, in emission order.
    /// Only scans inside BT/ET blocks so inflated font programs and other
    /// binary streams cannot contribute stray parenthesised bytes.
    /// </summary>
    public static string ExtractText(string decodedPdf)
    {
        var sb = new StringBuilder();
        int pos = 0;
        while (true)
        {
            int bt = decodedPdf.IndexOf("BT", pos, StringComparison.Ordinal);
            if (bt < 0) break;
            int et = decodedPdf.IndexOf("ET", bt, StringComparison.Ordinal);
            if (et < 0) break;

            int i = bt;
            while (i < et)
            {
                if (decodedPdf[i] != '(') { i++; continue; }

                // Read the literal string, honouring escapes.
                var literal = new StringBuilder();
                int j = i + 1;
                int depth = 1;
                while (j < et)
                {
                    char c = decodedPdf[j];
                    if (c == '\\' && j + 1 < et)
                    {
                        literal.Append(decodedPdf[j + 1]);
                        j += 2;
                        continue;
                    }
                    if (c == '(') depth++;
                    if (c == ')') { depth--; if (depth == 0) break; }
                    literal.Append(c);
                    j++;
                }

                // Only count it when a text-showing operator follows.
                int k = j + 1;
                while (k < et && (decodedPdf[k] == ' ' || decodedPdf[k] == '\n' ||
                                  decodedPdf[k] == '\r' || decodedPdf[k] == ']')) k++;
                if (k + 1 < et && decodedPdf[k] == 'T' &&
                    (decodedPdf[k + 1] == 'j' || decodedPdf[k + 1] == 'J'))
                    sb.Append(literal);

                i = j + 1;
            }
            pos = et + 2;
        }
        return sb.ToString();
    }

    public static string Decode(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf); // Latin-1 maps chars to bytes 1:1
        var sb = new StringBuilder(text.Length * 2);
        int pos = 0;

        while (true)
        {
            int filterIdx = text.IndexOf("/FlateDecode", pos, StringComparison.Ordinal);
            if (filterIdx < 0) break;
            int streamIdx = text.IndexOf("stream\n", filterIdx, StringComparison.Ordinal);
            if (streamIdx < 0) break;
            int dataStart = streamIdx + "stream\n".Length;
            int endIdx = text.IndexOf("endstream", dataStart, StringComparison.Ordinal);
            if (endIdx < 0) break;

            int dataEnd = endIdx;
            while (dataEnd > dataStart && (text[dataEnd - 1] == '\n' || text[dataEnd - 1] == '\r'))
                dataEnd--;

            sb.Append(text, pos, dataStart - pos);
            try
            {
                var raw = new byte[dataEnd - dataStart];
                for (int i = 0; i < raw.Length; i++) raw[i] = (byte)text[dataStart + i];
                // Skip the 2-byte zlib header; DeflateStream stops before the Adler-32.
                using var input = new MemoryStream(raw, 2, raw.Length - 2);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                deflate.CopyTo(output);
                sb.Append(Encoding.Latin1.GetString(output.ToArray()));
            }
            catch
            {
                sb.Append(text, dataStart, dataEnd - dataStart); // leave undecodable data as-is
            }
            sb.Append(text, dataEnd, endIdx - dataEnd);
            pos = endIdx;
        }

        sb.Append(text, pos, text.Length - pos);
        return sb.ToString();
    }
}
