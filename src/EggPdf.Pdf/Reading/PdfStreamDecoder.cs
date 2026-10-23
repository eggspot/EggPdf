using System;
using System.Collections.Generic;
using System.IO;

namespace EggPdf.Pdf;

internal static class PdfStreamDecoder
{
    public const int MaxDecodedBytes = 64 * 1024 * 1024;
    private const int InflateChunkBytes = 64 * 1024;

    public static byte[] Decode(PdfStream stream)
    {
        var filter = stream.Dictionary.Get("Filter");
        if (filter is PdfArray filters)
        {
            if (filters.Items.Count == 0) filter = null;
            else if (filters.Items.Count == 1) filter = filters.Items[0];
            else throw new PdfFormatException("Chained stream filters aren't supported in structural streams.");
        }

        if (filter == null)
        {
            var raw = new byte[stream.DataLength];
            Array.Copy(stream.Source, stream.DataStart, raw, 0, stream.DataLength);
            return raw;
        }
        if (filter is not PdfName name || (name.Value != "FlateDecode" && name.Value != "Fl"))
            throw new PdfFormatException("Only FlateDecode is supported in object and cross-reference streams.");

        var inflated = Inflate(stream.Source, stream.DataStart, stream.DataLength);
        var parameters = stream.Dictionary.Get("DecodeParms");
        if (parameters is PdfArray parameterList) parameters = parameterList.Items.Count > 0 ? parameterList.Items[0] : null;
        return parameters is PdfDictionary dictionary ? ApplyPredictor(inflated, dictionary) : inflated;
    }

    public static int IntegerOr(PdfObject? value, int fallback) =>
        value is PdfNumber number && number.IsInteger && number.Value >= int.MinValue && number.Value <= int.MaxValue
            ? (int)number.Value
            : fallback;

    private static byte[] Inflate(byte[] source, int start, int length)
    {
        int skip = length >= 2 && (source[start] & 0x0F) == 8 && ((source[start] << 8) | source[start + 1]) % 31 == 0 ? 2 : 0;
        try
        {
            using var input = new MemoryStream(source, start + skip, length - skip, writable: false);
            using var inflater = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
            var chunks = new List<byte[]>();
            var chunk = new byte[InflateChunkBytes];
            int filled = 0;
            long total = 0;
            int read;
            while ((read = inflater.Read(chunk, filled, chunk.Length - filled)) > 0)
            {
                total += read;
                if (total > MaxDecodedBytes) throw new PdfFormatException("A compressed stream expands beyond the 64 MB limit.");
                filled += read;
                if (filled < chunk.Length) continue;
                chunks.Add(chunk);
                chunk = new byte[InflateChunkBytes];
                filled = 0;
            }

            var result = new byte[total];
            int at = 0;
            foreach (var full in chunks)
            {
                Buffer.BlockCopy(full, 0, result, at, full.Length);
                at += full.Length;
            }
            Buffer.BlockCopy(chunk, 0, result, at, filled);
            return result;
        }
        catch (InvalidDataException ex)
        {
            throw new PdfFormatException("A compressed stream is corrupt.", ex);
        }
    }

    private static byte[] ApplyPredictor(byte[] data, PdfDictionary parameters)
    {
        int predictor = IntegerOr(parameters.Get("Predictor"), 1);
        if (predictor == 1) return data;
        if (predictor < 10) throw new PdfFormatException("TIFF predictors aren't supported in structural streams.");

        int colors = IntegerOr(parameters.Get("Colors"), 1);
        int bitsPerComponent = IntegerOr(parameters.Get("BitsPerComponent"), 8);
        int columns = IntegerOr(parameters.Get("Columns"), 1);
        if (colors < 1 || colors > 32 || bitsPerComponent < 1 || bitsPerComponent > 16 || columns < 1 || columns > 1 << 20)
            throw new PdfFormatException("Invalid predictor parameters.");
        int bytesPerPixel = Math.Max(1, colors * bitsPerComponent / 8);
        int rowLength = (colors * bitsPerComponent * columns + 7) / 8;
        if (rowLength + 1 > data.Length) return Array.Empty<byte>();

        int rows = data.Length / (rowLength + 1);
        var output = new byte[(long)rows * rowLength];
        var previous = new byte[rowLength];
        var row = new byte[rowLength];
        for (int r = 0, offset = 0; r < rows; r++, offset += 1 + rowLength)
        {
            int type = data[offset];
            for (int i = 0; i < rowLength; i++)
            {
                int raw = data[offset + 1 + i];
                int left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
                int up = previous[i];
                int upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
                row[i] = type switch
                {
                    0 => (byte)raw,
                    1 => (byte)(raw + left),
                    2 => (byte)(raw + up),
                    3 => (byte)(raw + (left + up) / 2),
                    4 => (byte)(raw + Paeth(left, up, upLeft)),
                    _ => throw new PdfFormatException("Invalid PNG predictor row type."),
                };
            }
            Buffer.BlockCopy(row, 0, output, r * rowLength, rowLength);
            var swap = previous;
            previous = row;
            row = swap;
        }
        return output;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
