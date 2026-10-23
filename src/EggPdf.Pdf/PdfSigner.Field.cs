using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EggPdf.Pdf;

public static partial class PdfSigner
{
    private const int ContentsHexLength = 16384;
    private const int HashBufferBytes = 80 * 1024;
    private const int ByteRangeWidth = 72;
    private const int PrintFlag = 4;
    private const int SignaturesExistAppendOnly = 3;
    private const int NoChangesAllowed = 1;
    private const double MaxExtent = 14400;
    private const double MinSize = 0.01;
    private const string ContentsMarker = "/Contents <";
    private const string ByteRangeMarker = "/ByteRange [";

    private sealed class PreparedSignature
    {
        public PreparedSignature(byte[] appended, int contentsStart, int byteRangeStart)
        {
            Appended = appended;
            ContentsStart = contentsStart;
            ByteRangeStart = byteRangeStart;
        }

        public byte[] Appended { get; }
        public int ContentsStart { get; }
        public int ByteRangeStart { get; }
    }

    private static PreparedSignature Prepare(PdfReader reader, SignOptions options, string signerName)
    {
        var store = reader.Store;
        if (reader.IsEncrypted)
            throw new NotSupportedException(
                "Signing an encrypted PDF is not supported — sign first, then note that " +
                "encrypting afterwards invalidates the signature; use one or the other.");
        if (store.WasRepaired)
            throw new PdfFormatException("The PDF's cross-reference is damaged: it can be read but not signed in place.");
        if (store.CertificationPermission == NoChangesAllowed)
            throw new NotSupportedException("The PDF is certified with a signature that forbids any change, including new signatures.");
        if (HasLockingSignature(store))
            throw new NotSupportedException("A signature in the PDF locks the document against further changes.");

        var catalogReference = store.Trailer.Get("Root") as PdfReference
            ?? throw new PdfFormatException("The document catalog isn't an indirect object and can't be revised.");
        var catalog = store.FindCatalog()!;
        var acroFormReference = catalog.Get("AcroForm") as PdfReference;
        var acroForm = (store.Resolve(catalog.Get("AcroForm")) as PdfDictionary)?.Clone() ?? new PdfDictionary();
        var fields = new PdfArray();
        if (store.Resolve(acroForm.Get("Fields")) is PdfArray existingFields) fields.Items.AddRange(existingFields.Items);

        var update = new PdfIncrementalUpdate(store);
        int signatureNumber = update.Allocate();
        if (options.FieldName != null && (options.FieldName.Length == 0 || options.FieldName.IndexOf('.') >= 0))
            throw new ArgumentException("A signature field name can't be empty or contain '.'; nested form fields aren't supported yet.", nameof(options));
        string fieldName = options.FieldName ?? FirstFreeFieldName(store, fields);
        var existing = FindField(store, fields, fieldName);
        if (existing != null)
        {
            var (reference, field) = existing.Value;
            if (store.Resolve(field.Get("FT")) is not PdfName { Value: "Sig" } || field.Get("V") != null)
                throw new ArgumentException($"The field '{fieldName}' isn't an empty signature field.", nameof(options));
            if (options.Appearance != null)
                throw new ArgumentException($"The field '{fieldName}' already has a position; leave Appearance unset to sign into it.", nameof(options));
            var signedField = field.Clone();
            signedField.Set("V", new PdfReference(signatureNumber, 0));
            update.WriteObject(reference.ObjectNumber, reference.Generation, signedField);
        }
        else
        {
            fields.Items.Add(AddField(update, reader, options, fieldName, signatureNumber));
        }

        acroForm.Set("Fields", fields);
        acroForm.Set("SigFlags", new PdfNumber(SignaturesExistAppendOnly));
        if (acroFormReference != null)
        {
            update.WriteObject(acroFormReference.ObjectNumber, acroFormReference.Generation, acroForm);
        }
        else
        {
            var revisedCatalog = catalog.Clone();
            revisedCatalog.Set("AcroForm", acroForm);
            update.WriteObject(catalogReference.ObjectNumber, catalogReference.Generation, revisedCatalog);
        }

        update.WriteRawObject(signatureNumber, SignatureDictionary(options, signerName));
        var appended = update.FinishAppendedBytes();
        return new PreparedSignature(appended, IndexOf(appended, ContentsMarker) + ContentsMarker.Length - 1,
            IndexOf(appended, ByteRangeMarker) + ByteRangeMarker.Length - 1);
    }

    private static PdfReference AddField(PdfIncrementalUpdate update, PdfReader reader, SignOptions options, string fieldName, int signatureNumber)
    {
        var appearance = options.Appearance;
        int pageNumber = appearance?.PageNumber ?? 1;
        if (pageNumber < 1 || pageNumber > reader.PageCount)
            throw new ArgumentOutOfRangeException(nameof(options), pageNumber, "The appearance page runs from 1 to the page count.");
        var page = reader.GetPageNode(pageNumber);
        var pageReference = page.Reference
            ?? throw new NotSupportedException("The page is written inline in its parent's /Kids and can't be revised.");

        int fieldNumber = update.Allocate();
        var widget = new PdfDictionary();
        widget.Set("Type", new PdfName("Annot"));
        widget.Set("Subtype", new PdfName("Widget"));
        widget.Set("FT", new PdfName("Sig"));
        widget.Set("T", TextString(fieldName));
        widget.Set("V", new PdfReference(signatureNumber, 0));
        widget.Set("F", new PdfNumber(PrintFlag));
        widget.Set("P", pageReference);
        var normal = new PdfDictionary();
        if (appearance == null)
        {
            widget.Set("Rect", Numbers(0, 0, 0, 0));
            int empty = update.Allocate();
            update.WriteStream(empty, "/Type /XObject /Subtype /Form /BBox [0 0 0 0]", Array.Empty<byte>());
            normal.Set("N", new PdfReference(empty, 0));
        }
        else
        {
            var displayed = new PdfDisplayedPage(page);
            ValidateBox(appearance);
            var image = PdfImage.Decode("Signature", appearance.Image)
                ?? throw new ArgumentException("Not a supported image: use PNG, JPEG, GIF, BMP or WebP.", nameof(options));
            double bottom = displayed.Height - appearance.Y - appearance.Height;
            widget.Set("Rect", Numbers(displayed.PageRectangle(appearance.X, bottom, appearance.Width, appearance.Height)));
            if (displayed.Rotation != 0)
            {
                var characteristics = new PdfDictionary();
                characteristics.Set("R", new PdfNumber(displayed.Rotation));
                widget.Set("MK", characteristics);
            }
            normal.Set("N", new PdfReference(WriteAppearance(update, appearance, image, displayed), 0));
        }
        widget.Set("AP", normal);
        update.WriteObject(fieldNumber, 0, widget);

        var annotations = new PdfArray();
        if (reader.Store.Resolve(page.Dictionary.Get("Annots")) is PdfArray existingAnnotations)
            annotations.Items.AddRange(existingAnnotations.Items);
        var fieldReference = new PdfReference(fieldNumber, 0);
        annotations.Items.Add(fieldReference);
        var revisedPage = page.Dictionary.Clone();
        revisedPage.Set("Annots", annotations);
        update.WriteObject(pageReference.ObjectNumber, pageReference.Generation, revisedPage);
        return fieldReference;
    }

    private static int WriteAppearance(PdfIncrementalUpdate update, SignatureAppearance appearance, PdfImage image, PdfDisplayedPage displayed)
    {
        int imageNumber = update.WriteImage(image);
        double width = appearance.Width, height = appearance.Height, left = 0, bottom = 0;
        if (appearance.Fit == PdfImageFit.Contain)
        {
            double scale = Math.Min(appearance.Width / image.Width, appearance.Height / image.Height);
            width = image.Width * scale;
            height = image.Height * scale;
            left = (appearance.Width - width) / 2;
            bottom = (appearance.Height - height) / 2;
        }
        var content = Encoding.ASCII.GetBytes("q " + PdfObjectSerializer.Numbers(width, 0, 0, height, left, bottom) + " cm /Img Do Q\n");
        int formNumber = update.Allocate();
        var rotation = displayed.ToPage;
        update.WriteStream(formNumber,
            "/Type /XObject /Subtype /Form /BBox [" + PdfObjectSerializer.Numbers(0, 0, appearance.Width, appearance.Height) + "]" +
            " /Matrix [" + PdfObjectSerializer.Numbers(rotation[0], rotation[1], rotation[2], rotation[3], 0, 0) + "]" +
            " /Resources << /XObject << /Img " + imageNumber.ToString(CultureInfo.InvariantCulture) + " 0 R >> >>",
            content);
        return formNumber;
    }

    private static void ValidateBox(SignatureAppearance appearance)
    {
        if (!InRange(appearance.X, -MaxExtent)) throw new ArgumentOutOfRangeException(nameof(appearance), appearance.X, "Positions run from -14,400 to 14,400 points.");
        if (!InRange(appearance.Y, -MaxExtent)) throw new ArgumentOutOfRangeException(nameof(appearance), appearance.Y, "Positions run from -14,400 to 14,400 points.");
        if (!InRange(appearance.Width, MinSize)) throw new ArgumentOutOfRangeException(nameof(appearance), appearance.Width, "Sizes run from 0.01 to 14,400 points.");
        if (!InRange(appearance.Height, MinSize)) throw new ArgumentOutOfRangeException(nameof(appearance), appearance.Height, "Sizes run from 0.01 to 14,400 points.");
    }

    private static bool InRange(double value, double minimum) => value >= minimum && value <= MaxExtent;

    private static string SignatureDictionary(SignOptions options, string signerName)
    {
        var dictionary = new StringBuilder("<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached");
        dictionary.Append(" /Name ").Append(PdfObjectSerializer.Serialize(TextString(signerName)));
        if (!string.IsNullOrEmpty(options.Reason)) dictionary.Append(" /Reason ").Append(PdfObjectSerializer.Serialize(TextString(options.Reason!)));
        if (!string.IsNullOrEmpty(options.Location)) dictionary.Append(" /Location ").Append(PdfObjectSerializer.Serialize(TextString(options.Location!)));
        dictionary.Append(" /M (").Append(PdfDates.PdfDate(DateTime.UtcNow)).Append(')');
        dictionary.Append(' ').Append(ByteRangeMarker).Append(new string(' ', ByteRangeWidth)).Append(']');
        dictionary.Append(' ').Append(ContentsMarker).Append(new string('0', ContentsHexLength)).Append('>');
        return dictionary.Append(" >>").ToString();
    }

    private static void WriteByteRange(PdfByteSource source, PreparedSignature update)
    {
        long gapStart = source.Length + update.ContentsStart;
        long gapEnd = gapStart + 2 + ContentsHexLength;
        long total = source.Length + update.Appended.Length;
        string range = "0 " + gapStart.ToString(CultureInfo.InvariantCulture) + " " + gapEnd.ToString(CultureInfo.InvariantCulture) + " " +
                       (total - gapEnd).ToString(CultureInfo.InvariantCulture);
        var bytes = Encoding.ASCII.GetBytes(("[" + range + "]").PadRight(ByteRangeWidth + 2));
        Buffer.BlockCopy(bytes, 0, update.Appended, update.ByteRangeStart, bytes.Length);
    }

    private static PdfString TextString(string text)
    {
        bool ascii = true;
        foreach (char c in text) ascii &= c >= 0x20 && c < 0x7F;
        if (ascii) return new PdfString(Encoding.ASCII.GetBytes(text));
        var utf16 = Encoding.BigEndianUnicode.GetBytes(text);
        var bytes = new byte[utf16.Length + 2];
        bytes[0] = 0xFE;
        bytes[1] = 0xFF;
        Buffer.BlockCopy(utf16, 0, bytes, 2, utf16.Length);
        return new PdfString(bytes);
    }

    private static PdfArray Numbers(params double[] values)
    {
        var array = new PdfArray();
        foreach (var value in values) array.Items.Add(new PdfNumber(value));
        return array;
    }

    private static string FirstFreeFieldName(PdfObjectStore store, PdfArray fields)
    {
        for (int suffix = 1; ; suffix++)
        {
            string name = "Signature" + suffix.ToString(CultureInfo.InvariantCulture);
            if (FindField(store, fields, name) == null) return name;
        }
    }

    private static (PdfReference Reference, PdfDictionary Field)? FindField(PdfObjectStore store, PdfArray fields, string name)
    {
        foreach (var item in fields.Items)
            if (item is PdfReference reference && store.Resolve(reference) is PdfDictionary field &&
                store.Resolve(field.Get("T")) is PdfString title && FieldTitle(title) == name)
                return (reference, field);
        return null;
    }

    private static string FieldTitle(PdfString title)
    {
        var bytes = title.Bytes;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++) chars[i] = (char)bytes[i];
        return new string(chars);
    }

    private static bool HasLockingSignature(PdfObjectStore store)
    {
        if (store.Resolve(store.FindCatalog()?.Get("AcroForm")) is not PdfDictionary form ||
            store.Resolve(form.Get("Fields")) is not PdfArray fields)
            return false;
        foreach (var item in fields.Items)
            if (store.Resolve(item) is PdfDictionary field && field.Get("V") != null &&
                store.Resolve(field.Get("Lock")) is PdfDictionary lockDictionary &&
                store.Resolve(lockDictionary.Get("P")) is PdfNumber { Value: NoChangesAllowed })
                return true;
        return false;
    }

    private static int IndexOf(byte[] data, string marker)
    {
        int found = PdfSyntaxParser.LastIndexOf(data, marker, data.Length);
        if (found < 0) throw new InvalidOperationException("The signature dictionary wasn't written.");
        return found;
    }
}
