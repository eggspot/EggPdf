using System;
using System.Collections.Generic;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>How an image fills the box given to <see cref="PdfStamper.AddImage"/>.</summary>
public enum PdfImageFit
{
    /// <summary>Fill the box exactly, stretching the image if its proportions differ.</summary>
    Stretch,

    /// <summary>Keep the image's proportions: as large as fits inside the box, centred in it.</summary>
    Contain,
}

/// <summary>
/// Places images onto pages of an existing PDF from any producer. The result is an incremental update appended to
/// the original bytes, so everything already in the file (fonts, annotations, form fields) stays as it was and an
/// earlier signature still verifies over the bytes it signed. Positions are in PDF points with the origin at the
/// top-left of the page as a viewer shows it (crop box and rotation applied), the same space as
/// <see cref="PdfReader.GetPage"/>. Encrypted files and files whose cross-reference had to be repaired can't be
/// stamped.
/// </summary>
public sealed class PdfStamper
{
    private const string ResourceNamePrefix = "EggPdfStamp";

    private sealed class Stamp
    {
        public Stamp(int pageNumber, PdfImage image, double x, double y, double width, double height, PdfImageFit fit)
        {
            PageNumber = pageNumber;
            Image = image;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Fit = fit;
        }

        public int PageNumber { get; }
        public PdfImage Image { get; }
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }
        public PdfImageFit Fit { get; }
    }

    private readonly byte[] _original;
    private readonly PdfReader _reader;
    private readonly List<Stamp> _stamps = new();

    /// <summary>Opens <paramref name="pdf"/> for stamping; the bytes aren't modified.</summary>
    /// <exception cref="PdfFormatException">The bytes aren't a readable PDF, or its cross-reference is damaged.</exception>
    /// <exception cref="NotSupportedException">The PDF is encrypted.</exception>
    public PdfStamper(byte[] pdf)
    {
        _original = pdf ?? throw new ArgumentNullException(nameof(pdf));
        _reader = PdfReader.Open(pdf);
        if (_reader.IsEncrypted) throw new NotSupportedException("Stamping an encrypted PDF isn't supported.");
        if (_reader.Store.WasRepaired)
            throw new PdfFormatException("The PDF's cross-reference is damaged: it can be read but not updated in place.");
    }

    /// <summary>
    /// Places <paramref name="image"/> (PNG, JPEG, GIF, BMP or WebP bytes) on page <paramref name="pageNumber"/> (1-based)
    /// in the box whose top-left corner is (<paramref name="x"/>, <paramref name="y"/>), in points from the top-left of
    /// the displayed page.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The page doesn't exist, or a size isn't a positive finite number.</exception>
    /// <exception cref="ArgumentException">The bytes aren't a supported image.</exception>
    public PdfStamper AddImage(int pageNumber, byte[] image, double x, double y, double width, double height,
        PdfImageFit fit = PdfImageFit.Stretch)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));
        if (pageNumber < 1 || pageNumber > _reader.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber, "Page numbers run from 1 to the page count.");
        if (!IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
        if (!IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(y));
        if (!IsFinite(width) || width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (!IsFinite(height) || height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        var decoded = DecodeImage(image)
            ?? throw new ArgumentException("Not a supported image: use PNG, JPEG, GIF, BMP or WebP.", nameof(image));
        _stamps.Add(new Stamp(pageNumber, decoded, x, y, width, height, fit));
        return this;
    }

    /// <summary>Returns the original PDF with the images added as an incremental update (the input itself when nothing was added).</summary>
    public byte[] Build()
    {
        if (_stamps.Count == 0) return (byte[])_original.Clone();

        var update = new PdfIncrementalUpdate(_original, _reader.Store);
        int saveState = update.Allocate();
        update.WriteStream(saveState, "", Encoding.ASCII.GetBytes("q\n"));

        var byPage = new SortedDictionary<int, List<Stamp>>();
        foreach (var stamp in _stamps)
        {
            if (!byPage.TryGetValue(stamp.PageNumber, out var list)) byPage[stamp.PageNumber] = list = new List<Stamp>();
            list.Add(stamp);
        }

        foreach (var pair in byPage)
            StampPage(update, _reader.GetPageNode(pair.Key), pair.Value, saveState);
        return update.Finish();
    }

    private void StampPage(PdfIncrementalUpdate update, PdfPageNode page, List<Stamp> stamps, int saveState)
    {
        var reference = page.Reference
            ?? throw new NotSupportedException("The page is written inline in its parent's /Kids and can't be revised.");
        var store = _reader.Store;

        var resources = Copy(store.Resolve(page.Resources) as PdfDictionary);
        var xObjects = Copy(store.Resolve(resources.Get("XObject")) as PdfDictionary);
        var operators = new StringBuilder("Q\n");
        int suffix = 1;
        foreach (var stamp in stamps)
        {
            string name;
            do name = ResourceNamePrefix + (suffix++).ToString(System.Globalization.CultureInfo.InvariantCulture);
            while (xObjects.Get(name) != null);
            int imageNumber = WriteImage(update, stamp.Image);
            xObjects.Set(name, new PdfReference(imageNumber, 0));
            operators.Append(Placement(page, stamp)).Append(" /").Append(name).Append(" Do Q\n");
        }
        resources.Set("XObject", xObjects);

        int stampContent = update.Allocate();
        update.WriteStream(stampContent, "", Encoding.ASCII.GetBytes(operators.ToString()));

        var contents = new PdfArray();
        contents.Items.Add(new PdfReference(saveState, 0));
        var original = page.Dictionary.Get("Contents");
        if (store.Resolve(original) is PdfArray originalArray) contents.Items.AddRange(originalArray.Items);
        else if (original != null) contents.Items.Add(original);
        contents.Items.Add(new PdfReference(stampContent, 0));

        var revised = Copy(page.Dictionary);
        revised.Set("Resources", resources);
        revised.Set("Contents", contents);
        update.WriteObject(reference.ObjectNumber, reference.Generation, revised);
    }

    private static string Placement(PdfPageNode page, Stamp stamp)
    {
        var box = page.VisibleBox;
        double boxWidth = box[2] - box[0], boxHeight = box[3] - box[1];
        double displayedHeight = page.Rotation % 180 == 0 ? boxHeight : boxWidth;

        double width = stamp.Width, height = stamp.Height, left = stamp.X, bottom = displayedHeight - stamp.Y - stamp.Height;
        if (stamp.Fit == PdfImageFit.Contain)
        {
            double scale = Math.Min(stamp.Width / stamp.Image.Width, stamp.Height / stamp.Image.Height);
            width = stamp.Image.Width * scale;
            height = stamp.Image.Height * scale;
            left += (stamp.Width - width) / 2;
            bottom += (stamp.Height - height) / 2;
        }

        double[] toPage = page.Rotation switch
        {
            90 => new[] { 0.0, 1, -1, 0, boxWidth, 0 },
            180 => new[] { -1.0, 0, 0, -1, boxWidth, boxHeight },
            270 => new[] { 0.0, -1, 1, 0, 0, boxHeight },
            _ => new[] { 1.0, 0, 0, 1, 0, 0 },
        };
        toPage[4] += box[0];
        toPage[5] += box[1];

        return "q " + Join(toPage) + " cm " + Join(new[] { width, 0, 0, height, left, bottom }) + " cm";
    }

    private static int WriteImage(PdfIncrementalUpdate update, PdfImage image)
    {
        int number = update.Allocate();
        string size = "/Type /XObject /Subtype /Image /Width " + image.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)
                      + " /Height " + image.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (image.Format == PdfImageFormat.Jpeg)
        {
            update.WriteStream(number, size + " /ColorSpace " + image.ColorSpace + " /BitsPerComponent 8" + image.DecodeEntry + " /Filter /DCTDecode", image.Data);
            return number;
        }

        string softMask = "";
        if (image.SMaskData != null)
        {
            int maskNumber = update.Allocate();
            update.WriteStream(maskNumber, size + " /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode",
                PdfDocument.CompressZlib(image.SMaskData));
            softMask = " /SMask " + maskNumber.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 R";
        }
        update.WriteStream(number, size + " /ColorSpace /DeviceRGB /BitsPerComponent "
            + image.BitsPerComponent.ToString(System.Globalization.CultureInfo.InvariantCulture) + " /Filter /FlateDecode" + softMask,
            PdfDocument.CompressZlib(image.Data));
        return number;
    }

    private static PdfImage? DecodeImage(byte[] data)
    {
        const string name = "Stamp";
        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G') return PdfImage.FromPng(name, data);
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8) return PdfImage.FromJpeg(name, data);
        if (data.Length >= 4 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8') return PdfImage.FromGif(name, data);
        if (data.Length >= 2 && data[0] == 'B' && data[1] == 'M') return PdfImage.FromBmp(name, data);
        if (WebPDecoder.IsWebP(data)) return PdfImage.FromWebP(name, data);
        return null;
    }

    private static PdfDictionary Copy(PdfDictionary? source)
    {
        var copy = new PdfDictionary();
        if (source != null)
            foreach (var pair in source.Entries) copy.Set(pair.Key, pair.Value);
        return copy;
    }

    private static string Join(double[] values)
    {
        var parts = new string[values.Length];
        for (int i = 0; i < values.Length; i++) parts[i] = PdfObjectSerializer.Number(values[i]);
        return string.Join(" ", parts);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
