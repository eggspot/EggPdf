using System;
using System.Collections.Generic;
using System.IO;
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
/// <see cref="PdfReader.GetPage"/>. Encrypted files, files whose cross-reference had to be repaired and certified
/// (DocMDP) files can't be stamped. Stamps are marked as artifacts, so a tagged PDF's structure doesn't include them.
/// </summary>
public sealed class PdfStamper
{
    private const string ResourceNamePrefix = "EggPdfStamp";
    private const double MaxExtent = 14400;
    private const double MinSize = 0.01;
    private const long MaxArrayBytes = 0x7FFFFFC7;

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

    private readonly PdfReader _reader;
    private readonly List<Stamp> _stamps = new();
    private readonly List<(byte[] Source, PdfImage Decoded)> _images = new();

    /// <summary>Opens <paramref name="pdf"/> for stamping; the bytes aren't modified.</summary>
    /// <exception cref="PdfFormatException">The bytes aren't a readable PDF, or its cross-reference is damaged.</exception>
    /// <exception cref="NotSupportedException">The PDF is encrypted, or certified with a DocMDP signature.</exception>
    public PdfStamper(byte[] pdf) : this(PdfReader.Open(pdf ?? throw new ArgumentNullException(nameof(pdf))))
    {
    }

    /// <summary>
    /// Opens the PDF in <paramref name="pdf"/> (readable and seekable) for stamping without loading it into memory, so
    /// files of any size can be stamped; pair it with <see cref="Build(Stream)"/>. The stream isn't modified or closed and
    /// must stay open and unchanged until the stamper has been built.
    /// </summary>
    /// <exception cref="ArgumentException">The stream isn't readable and seekable.</exception>
    /// <exception cref="PdfFormatException">The stream isn't a readable PDF, or its cross-reference is damaged.</exception>
    /// <exception cref="NotSupportedException">The PDF is encrypted, or certified with a DocMDP signature.</exception>
    public PdfStamper(Stream pdf) : this(PdfReader.Open(pdf))
    {
    }

    private PdfStamper(PdfReader reader)
    {
        _reader = reader;
        if (_reader.IsEncrypted) throw new NotSupportedException("Stamping an encrypted PDF isn't supported.");
        if (_reader.Store.WasRepaired)
            throw new PdfFormatException("The PDF's cross-reference is damaged: it can be read but not updated in place.");
        if (IsCertified(_reader.Store))
            throw new NotSupportedException(
                "The PDF is certified (a DocMDP signature): no certification level permits new page content, so stamping would invalidate it.");
    }

    /// <summary>
    /// Places <paramref name="image"/> (PNG, JPEG, GIF, BMP or WebP bytes) on page <paramref name="pageNumber"/> (1-based)
    /// in the box whose top-left corner is (<paramref name="x"/>, <paramref name="y"/>), in points from the top-left of
    /// the displayed page.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The page doesn't exist, a position is outside ±14,400 points, or a size is outside 0.01 to 14,400 points
    /// (14,400 points is the largest page side PDF allows).
    /// </exception>
    /// <exception cref="ArgumentException">The bytes aren't a supported image.</exception>
    public PdfStamper AddImage(int pageNumber, byte[] image, double x, double y, double width, double height,
        PdfImageFit fit = PdfImageFit.Stretch)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));
        if (pageNumber < 1 || pageNumber > _reader.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber, "Page numbers run from 1 to the page count.");
        if (!InRange(x, -MaxExtent)) throw new ArgumentOutOfRangeException(nameof(x), x, "Positions run from -14,400 to 14,400 points.");
        if (!InRange(y, -MaxExtent)) throw new ArgumentOutOfRangeException(nameof(y), y, "Positions run from -14,400 to 14,400 points.");
        if (!InRange(width, MinSize)) throw new ArgumentOutOfRangeException(nameof(width), width, "Sizes run from 0.01 to 14,400 points.");
        if (!InRange(height, MinSize)) throw new ArgumentOutOfRangeException(nameof(height), height, "Sizes run from 0.01 to 14,400 points.");
        _stamps.Add(new Stamp(pageNumber, Decoded(image), x, y, width, height, fit));
        return this;
    }

    private PdfImage Decoded(byte[] image)
    {
        foreach (var (source, decoded) in _images)
            if (SameBytes(source, image)) return decoded;
        var result = DecodeImage(image)
            ?? throw new ArgumentException("Not a supported image: use PNG, JPEG, GIF, BMP or WebP.", nameof(image));
        _images.Add((image, result));
        return result;
    }

    /// <summary>Returns the original PDF with the images added as an incremental update (a copy of the input when nothing was added).</summary>
    /// <exception cref="PdfFormatException">
    /// A stamped page's part of the page tree is damaged, or the update would take the file past the 8,388,607 objects a
    /// PDF can hold.
    /// </exception>
    /// <exception cref="InvalidOperationException">The result is too large for a byte array; use <see cref="Build(Stream)"/>.</exception>
    public byte[] Build()
    {
        var appended = AppendedBytes();
        long total = _reader.Store.Source.Length + appended.Length;
        if (total > MaxArrayBytes) throw new InvalidOperationException("The stamped PDF is too large for a byte array: use Build(Stream).");
        var result = new byte[total];
        using (var output = new MemoryStream(result)) WriteTo(output, appended);
        return result;
    }

    /// <summary>
    /// Writes the original PDF followed by the images as an incremental update to <paramref name="output"/>. Only the
    /// update is held in memory, so this is the way to stamp large files.
    /// </summary>
    /// <exception cref="PdfFormatException">
    /// A stamped page's part of the page tree is damaged, or the update would take the file past the 8,388,607 objects a
    /// PDF can hold.
    /// </exception>
    public void Build(Stream output)
    {
        if (output == null) throw new ArgumentNullException(nameof(output));
        WriteTo(output, AppendedBytes());
    }

    private void WriteTo(Stream output, byte[] appended)
    {
        _reader.Store.Source.CopyTo(output);
        output.Write(appended, 0, appended.Length);
    }

    private byte[] AppendedBytes()
    {
        if (_stamps.Count == 0) return Array.Empty<byte>();

        var update = new PdfIncrementalUpdate(_reader.Store);
        int saveState = update.Allocate();
        update.WriteStream(saveState, "", Encoding.ASCII.GetBytes("q\n"));

        var byPage = new SortedDictionary<int, List<Stamp>>();
        foreach (var stamp in _stamps)
        {
            if (!byPage.TryGetValue(stamp.PageNumber, out var list)) byPage[stamp.PageNumber] = list = new List<Stamp>();
            list.Add(stamp);
        }

        var imageNumbers = new Dictionary<PdfImage, int>();
        foreach (var pair in byPage)
            StampPage(update, _reader.GetPageNode(pair.Key), pair.Value, saveState, imageNumbers);
        return update.FinishAppendedBytes();
    }

    private void StampPage(PdfIncrementalUpdate update, PdfPageNode page, List<Stamp> stamps, int saveState,
        Dictionary<PdfImage, int> imageNumbers)
    {
        var reference = page.Reference
            ?? throw new NotSupportedException("The page is written inline in its parent's /Kids and can't be revised.");
        var store = _reader.Store;

        var resources = Copy(store.Resolve(page.Resources) as PdfDictionary);
        var xObjects = Copy(store.Resolve(resources.Get("XObject")) as PdfDictionary);
        var operators = new StringBuilder("\nQ\n");
        int suffix = 1;
        foreach (var stamp in stamps)
        {
            string name;
            do name = ResourceNamePrefix + (suffix++).ToString(System.Globalization.CultureInfo.InvariantCulture);
            while (xObjects.Get(name) != null);
            if (!imageNumbers.TryGetValue(stamp.Image, out int imageNumber))
                imageNumbers[stamp.Image] = imageNumber = WriteImage(update, stamp.Image);
            xObjects.Set(name, new PdfReference(imageNumber, 0));
            operators.Append("/Artifact BMC ").Append(Placement(page, stamp)).Append(" /").Append(name).Append(" Do Q EMC\n");
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

    private static bool InRange(double value, double minimum) => value >= minimum && value <= MaxExtent;

    private static bool SameBytes(byte[] a, byte[] b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private static bool IsCertified(PdfObjectStore store) =>
        store.Resolve(store.FindCatalog()?.Get("Perms")) is PdfDictionary permissions && permissions.Get("DocMDP") != null;
}
