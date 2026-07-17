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
