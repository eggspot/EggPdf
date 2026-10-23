using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Signs PDF documents with a detached CMS/PKCS#7 signature (adbe.pkcs7.detached) in a real signature field: the
/// signature is appended as an incremental update with a signature field and widget on the page, the document's
/// AcroForm and a cross-reference section, so viewers and validators list it and earlier signatures keep verifying.
/// Signatures can be invisible (the default) or show an image at a position on a page.
/// </summary>
public static partial class PdfSigner
{
    /// <summary>Options for PDF signing.</summary>
    public class SignOptions
    {
        /// <summary>Reason for signing (e.g., "Approved").</summary>
        public string? Reason { get; set; }
        /// <summary>Location of signing (e.g., "New York").</summary>
        public string? Location { get; set; }
        /// <summary>Signer name.</summary>
        public string? Name { get; set; }

        /// <summary>
        /// Name of the signature field. When the document already has an empty signature field with this name, the
        /// signature goes into it (keeping its position); otherwise a new field is added. Defaults to the first unused
        /// "Signature1", "Signature2", ...
        /// </summary>
        public string? FieldName { get; set; }

        /// <summary>Where and how the signature shows on a page; <c>null</c> (the default) signs invisibly.</summary>
        public SignatureAppearance? Appearance { get; set; }
    }

    /// <summary>
    /// A visible signature: an image (PNG, JPEG, GIF, BMP or WebP, e.g. a handwritten signature) in a box on a page.
    /// Positions are in PDF points from the top-left of the page as a viewer shows it (crop box and rotation applied),
    /// like <see cref="PdfStamper.AddImage"/>. Viewers show the signer, date and reason from the signature itself.
    /// </summary>
    public sealed class SignatureAppearance
    {
        /// <summary>Shows <paramref name="image"/> in the box at (<paramref name="x"/>, <paramref name="y"/>) on page <paramref name="pageNumber"/> (1-based).</summary>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is null.</exception>
        public SignatureAppearance(int pageNumber, double x, double y, double width, double height, byte[] image)
        {
            PageNumber = pageNumber;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Image = image ?? throw new ArgumentNullException(nameof(image));
        }

        /// <summary>The page the signature shows on (1-based).</summary>
        public int PageNumber { get; }
        /// <summary>Left edge of the box, in points from the left of the displayed page.</summary>
        public double X { get; }
        /// <summary>Top edge of the box, in points from the top of the displayed page.</summary>
        public double Y { get; }
        /// <summary>Width of the box in points.</summary>
        public double Width { get; }
        /// <summary>Height of the box in points.</summary>
        public double Height { get; }
        /// <summary>The image bytes.</summary>
        public byte[] Image { get; }
        /// <summary>How the image fills the box; <see cref="PdfImageFit.Contain"/> (keep proportions) by default.</summary>
        public PdfImageFit Fit { get; set; } = PdfImageFit.Contain;
    }

    /// <summary>
    /// Sign a PDF in one call with an X.509 certificate (RSA or ECDSA private key). The result is the input followed
    /// by an incremental update holding the signature field and a detached CMS signature over the whole file.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="certificate"/> is null.</exception>
    /// <exception cref="PdfFormatException">The PDF can't be read, or its cross-reference is damaged so it can't be updated in place.</exception>
    /// <exception cref="NotSupportedException">The PDF is encrypted, or certified in a way that forbids changes or locks signing.</exception>
    /// <exception cref="ArgumentException">The field name belongs to a field that isn't an empty signature field, or the appearance image isn't supported.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The appearance page or box is out of range.</exception>
    public static byte[] Sign(byte[] pdfBytes, X509Certificate2 certificate, SignOptions? options = null)
    {
        if (pdfBytes == null || pdfBytes.Length == 0) return Array.Empty<byte>();
        if (certificate == null) throw new ArgumentNullException(nameof(certificate));
        using var reader = PdfReader.Open(pdfBytes);
        var update = Prepare(reader, options ?? new SignOptions(), SignerName(options, certificate));
        Seal(reader.Store.Source, update, certificate);
        return Concatenate(reader.Store.Source, update);
    }

    /// <summary>
    /// Sign the PDF read from <paramref name="pdf"/> (readable and seekable) and write the signed PDF to
    /// <paramref name="output"/>, holding only the small update in memory, so files of any size can be signed. The
    /// input must start at position 0 and isn't modified or closed.
    /// </summary>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The input stream isn't readable and seekable, or an option is invalid (see the byte[] overload).</exception>
    /// <exception cref="PdfFormatException">The PDF can't be read, or its cross-reference is damaged so it can't be updated in place.</exception>
    /// <exception cref="NotSupportedException">The PDF is encrypted, or certified in a way that forbids changes or locks signing.</exception>
    public static void Sign(Stream pdf, Stream output, X509Certificate2 certificate, SignOptions? options = null)
    {
        if (output == null) throw new ArgumentNullException(nameof(output));
        if (certificate == null) throw new ArgumentNullException(nameof(certificate));
        using var reader = PdfReader.Open(pdf);
        var update = Prepare(reader, options ?? new SignOptions(), SignerName(options, certificate));
        Seal(reader.Store.Source, update, certificate);
        reader.Store.Source.CopyTo(output);
        output.Write(update.Appended, 0, update.Appended.Length);
    }

    /// <summary>
    /// Sign a PDF in one call with a certificate and private key (RSA or ECDSA) from PFX/PKCS#12 bytes; same
    /// signature as <see cref="Sign(byte[], X509Certificate2, SignOptions?)"/>. <paramref name="password"/> is null
    /// or empty when the PFX has none. To sign many documents, load once with <see cref="PdfSigningCertificate.FromPfx"/>.
    /// </summary>
    /// <exception cref="CryptographicException">The bytes aren't a PKCS#12 file or the password is wrong.</exception>
    public static byte[] Sign(byte[] pdfBytes, byte[] pfx, string? password, SignOptions? options = null)
    {
        using var certificate = PdfSigningCertificate.FromPfx(pfx, password);
        return Sign(pdfBytes, certificate, options);
    }

    /// <summary>
    /// Sign a PDF in one call with a PEM certificate and PEM private key (RSA or ECDSA; PKCS#8, PKCS#1, SEC1, or
    /// encrypted PKCS#8 with <paramref name="privateKeyPassword"/>). The pair is loaded through
    /// <see cref="PdfSigningCertificate.FromPem"/>, so it behaves exactly like a PFX. Not available on .NET Standard 2.0.
    /// </summary>
    /// <exception cref="CryptographicException">The PEM can't be read, the password is wrong, or the key doesn't match the certificate.</exception>
    /// <exception cref="PlatformNotSupportedException">Running on the .NET Standard 2.0 build.</exception>
    public static byte[] SignWithPem(byte[] pdfBytes, string certificatePem, string privateKeyPem,
        string? privateKeyPassword = null, SignOptions? options = null)
    {
        using var certificate = PdfSigningCertificate.FromPem(certificatePem, privateKeyPem, privateKeyPassword);
        return Sign(pdfBytes, certificate, options);
    }

    /// <summary>
    /// Adds a signature field whose signature value is a placeholder, for signing outside this library: hash the
    /// bytes its <c>/ByteRange</c> lists (everything but the <c>/Contents</c> value) with SHA-256, build a detached
    /// CMS over that digest, then call <see cref="SignWithCmsData"/>.
    /// </summary>
    /// <exception cref="PdfFormatException">The PDF can't be read, or its cross-reference is damaged so it can't be updated in place.</exception>
    /// <exception cref="NotSupportedException">The PDF is encrypted, or certified in a way that forbids changes or locks signing.</exception>
    public static byte[] AddSignaturePlaceholder(byte[] pdfBytes, SignOptions? options = null)
    {
        if (pdfBytes == null || pdfBytes.Length == 0) return pdfBytes ?? Array.Empty<byte>();
        options ??= new SignOptions();
        using var reader = PdfReader.Open(pdfBytes);
        var update = Prepare(reader, options, options.Name ?? "Signer");
        WriteByteRange(reader.Store.Source, update);
        return Concatenate(reader.Store.Source, update);
    }

    /// <summary>
    /// Fills the placeholder that <see cref="AddSignaturePlaceholder"/> added with <paramref name="cmsSignature"/>
    /// (detached CMS/PKCS#7 bytes). The file keeps its length, so the byte range stays valid.
    /// </summary>
    /// <exception cref="ArgumentException">The PDF has no signature placeholder.</exception>
    /// <exception cref="InvalidOperationException">The CMS is larger than the reserved placeholder (8 KB).</exception>
    public static byte[] SignWithCmsData(byte[] pdfWithPlaceholder, byte[] cmsSignature)
    {
        if (pdfWithPlaceholder == null || cmsSignature == null)
            return pdfWithPlaceholder ?? Array.Empty<byte>();
        var result = (byte[])pdfWithPlaceholder.Clone();
        if (!TryFindLastContents(result, out int hexStart, out int hexLength))
            throw new ArgumentException("The PDF has no signature placeholder.", nameof(pdfWithPlaceholder));
        for (int i = 0; i < hexLength; i++)
            if (result[hexStart + i] != '0')
                throw new ArgumentException("The last signature is already filled in; add a placeholder first.", nameof(pdfWithPlaceholder));
        WriteHex(result, hexStart, hexLength, cmsSignature);
        return result;
    }

    private static string SignerName(SignOptions? options, X509Certificate2 certificate) =>
        options?.Name ?? certificate.GetNameInfo(X509NameType.SimpleName, false) ?? "Signer";

    private static byte[] Concatenate(PdfByteSource source, PreparedSignature update)
    {
        var result = new byte[source.Length + update.Appended.Length];
        using (var output = new MemoryStream(result)) source.CopyTo(output);
        Buffer.BlockCopy(update.Appended, 0, result, (int)source.Length, update.Appended.Length);
        return result;
    }

    private static void Seal(PdfByteSource source, PreparedSignature update, X509Certificate2 certificate)
    {
        WriteByteRange(source, update);
        byte[] digest;
        using (var sha = SHA256.Create())
        {
            var buffer = new byte[HashBufferBytes];
            for (long position = 0; position < source.Length;)
            {
                int length = (int)Math.Min(buffer.Length, source.Length - position);
                source.Read(position, buffer, length);
                sha.TransformBlock(buffer, 0, length, null, 0);
                position += length;
            }
            int gapStart = update.ContentsStart;
            int gapEnd = gapStart + 2 + ContentsHexLength;
            sha.TransformBlock(update.Appended, 0, gapStart, null, 0);
            sha.TransformFinalBlock(update.Appended, gapEnd, update.Appended.Length - gapEnd);
            digest = sha.Hash!;
        }
        var cms = CmsSignedDataBuilder.BuildDetached(digest, certificate, DateTime.UtcNow);
        WriteHex(update.Appended, update.ContentsStart + 1, ContentsHexLength, cms);
    }

    private static void WriteHex(byte[] target, int hexStart, int hexLength, byte[] value)
    {
        if (value.Length * 2 > hexLength) throw new InvalidOperationException("CMS signature exceeds the reserved /Contents space.");
        const string digits = "0123456789ABCDEF";
        for (int i = 0; i < value.Length; i++)
        {
            target[hexStart + 2 * i] = (byte)digits[value[i] >> 4];
            target[hexStart + 2 * i + 1] = (byte)digits[value[i] & 0xF];
        }
        for (int i = value.Length * 2; i < hexLength; i++) target[hexStart + i] = (byte)'0';
    }

    private static bool TryFindLastContents(byte[] pdf, out int hexStart, out int hexLength)
    {
        hexStart = hexLength = 0;
        int byteRange = PdfSyntaxParser.LastIndexOf(pdf, "/ByteRange", pdf.Length);
        if (byteRange < 0) return false;
        var parser = new PdfSyntaxParser(pdf, byteRange + "/ByteRange".Length, pdf.Length);
        if (parser.ReadObject() is not PdfArray range || range.Items.Count != 4) return false;
        long gapStart = PdfStreamDecoder.IntegerOr(range.Items[1], -1), gapEnd = PdfStreamDecoder.IntegerOr(range.Items[2], -1);
        if (gapStart < 0 || gapEnd <= gapStart + 1 || gapEnd > pdf.Length || pdf[gapStart] != '<' || pdf[gapEnd - 1] != '>') return false;
        hexStart = (int)gapStart + 1;
        hexLength = (int)(gapEnd - gapStart - 2);
        return true;
    }
}
