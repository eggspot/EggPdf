using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Signs PDF documents with digital signatures.
/// Adds a signature dictionary to the PDF with placeholder for CMS/PKCS#7 data.
/// For full PAdES compliance, use SignWithCmsData() with externally-computed CMS bytes.
/// </summary>
public static class PdfSigner
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
    }

    private const int ContentsHexLength = 16384;

    /// <summary>
    /// Sign a PDF in one call with an X.509 certificate (RSA private key).
    /// Embeds a detached CMS/PKCS#7 signature (adbe.pkcs7.detached) with a
    /// correct /ByteRange, verifiable by standard PDF/CMS validators.
    /// </summary>
    public static byte[] Sign(byte[] pdfBytes, X509Certificate2 certificate, SignOptions? options = null)
    {
        if (pdfBytes == null || pdfBytes.Length == 0) return Array.Empty<byte>();
        if (certificate == null) throw new ArgumentNullException(nameof(certificate));
        ThrowIfEncrypted(pdfBytes);

        options ??= new SignOptions();
        string name = options.Name
            ?? certificate.GetNameInfo(X509NameType.SimpleName, false)
            ?? "Signer";

        // 1. Append the signature dictionary as an incremental update, with a
        //    fixed-width ByteRange placeholder patched after assembly.
        const string byteRangePlaceholder = "[0 0000000000 0000000000 0000000000]";
        var sb = new StringBuilder();
        sb.AppendLine();
        int sigValObj = FindNextObjNumber(pdfBytes) + 1;
        sb.AppendLine($"{sigValObj} 0 obj");
        sb.Append("<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached");
        sb.Append($" /Name ({EscapePdf(name)})");
        if (!string.IsNullOrEmpty(options.Reason))
            sb.Append($" /Reason ({EscapePdf(options.Reason!)})");
        if (!string.IsNullOrEmpty(options.Location))
            sb.Append($" /Location ({EscapePdf(options.Location!)})");
        sb.Append($" /M (D:{DateTime.UtcNow:yyyyMMddHHmmss}Z)");
        sb.Append($" /ByteRange {byteRangePlaceholder}");
        sb.Append($" /Contents <{new string('0', ContentsHexLength)}>");
        sb.AppendLine(" >>");
        sb.AppendLine("endobj");

        byte[] output;
        using (var ms = new MemoryStream())
        {
            ms.Write(pdfBytes, 0, pdfBytes.Length);
            var append = Encoding.ASCII.GetBytes(sb.ToString());
            ms.Write(append, 0, append.Length);
            output = ms.ToArray();
        }

        // 2. Locate the Contents hex gap and patch the real ByteRange
        var text = Encoding.ASCII.GetString(output, pdfBytes.Length, output.Length - pdfBytes.Length);
        int hexInAppend = text.IndexOf(new string('0', ContentsHexLength), StringComparison.Ordinal);
        int hexStart = pdfBytes.Length + hexInAppend;
        int gapStart = hexStart - 1;                     // the '<'
        int gapEnd = hexStart + ContentsHexLength + 1;   // after the '>'
        int tailLength = output.Length - gapEnd;

        string byteRange = $"[0 {gapStart:D10} {gapEnd:D10} {tailLength:D10}]";
        int brInAppend = text.IndexOf(byteRangePlaceholder, StringComparison.Ordinal);
        var brBytes = Encoding.ASCII.GetBytes(byteRange);
        Array.Copy(brBytes, 0, output, pdfBytes.Length + brInAppend, brBytes.Length);

        // 3. Hash the two byte ranges and build the detached CMS signature
        byte[] digest;
        using (var sha = SHA256.Create())
        {
            sha.TransformBlock(output, 0, gapStart, null, 0);
            sha.TransformFinalBlock(output, gapEnd, tailLength);
            digest = sha.Hash!;
        }

        var cms = CmsSignedDataBuilder.BuildDetached(digest, certificate, DateTime.UtcNow);
        if (cms.Length * 2 > ContentsHexLength)
            throw new InvalidOperationException("CMS signature exceeds the reserved /Contents space.");

        // 4. Write the CMS hex into the reserved gap
        var cmsHex = Encoding.ASCII.GetBytes(BitConverter.ToString(cms).Replace("-", ""));
        Array.Copy(cmsHex, 0, output, hexStart, cmsHex.Length);

        return output;
    }

    /// <summary>
    /// Add a signature placeholder to a PDF.
    /// The caller can then compute the CMS signature externally and fill it in.
    /// Returns the PDF with an empty signature field.
    /// </summary>
    public static byte[] AddSignaturePlaceholder(byte[] pdfBytes, SignOptions? options = null)
    {
        if (pdfBytes == null || pdfBytes.Length == 0) return pdfBytes ?? Array.Empty<byte>();
        ThrowIfEncrypted(pdfBytes);

        options ??= new SignOptions();
        string name = options.Name ?? "Signer";
        string reason = options.Reason ?? "";
        string location = options.Location ?? "";

        // Create signature annotation as an incremental update
        var sb = new StringBuilder();
        sb.AppendLine(); // newline separator

        // Signature value object
        int sigValObj = FindNextObjNumber(pdfBytes) + 1;
        sb.AppendLine($"{sigValObj} 0 obj");
        sb.Append("<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached");
        sb.Append($" /Name ({EscapePdf(name)})");
        if (!string.IsNullOrEmpty(reason))
            sb.Append($" /Reason ({EscapePdf(reason)})");
        if (!string.IsNullOrEmpty(location))
            sb.Append($" /Location ({EscapePdf(location)})");
        sb.Append($" /M (D:{DateTime.UtcNow:yyyyMMddHHmmss}Z)");
        sb.Append($" /Contents <{new string('0', 16384)}>"); // 8KB placeholder
        sb.Append($" /ByteRange [0 {pdfBytes.Length} {pdfBytes.Length} 0]");
        sb.AppendLine(" >>");
        sb.AppendLine("endobj");

        using var ms = new MemoryStream();
        ms.Write(pdfBytes, 0, pdfBytes.Length);
        var appendBytes = Encoding.ASCII.GetBytes(sb.ToString());
        ms.Write(appendBytes, 0, appendBytes.Length);

        return ms.ToArray();
    }

    /// <summary>
    /// Sign a PDF with pre-computed CMS/PKCS#7 signature bytes.
    /// First call AddSignaturePlaceholder(), compute the CMS hash externally,
    /// then call this to insert the signature data.
    /// </summary>
    public static byte[] SignWithCmsData(byte[] pdfWithPlaceholder, byte[] cmsSignature)
    {
        if (pdfWithPlaceholder == null || cmsSignature == null)
            return pdfWithPlaceholder ?? Array.Empty<byte>();

        // Find the Contents placeholder in the PDF
        var pdfText = Encoding.ASCII.GetString(pdfWithPlaceholder);
        var placeholder = new string('0', 16384);
        int placeholderIdx = pdfText.IndexOf(placeholder, StringComparison.Ordinal);
        if (placeholderIdx < 0) return pdfWithPlaceholder;

        // Convert CMS signature to hex
        string cmsHex = BitConverter.ToString(cmsSignature).Replace("-", "");
        if (cmsHex.Length > placeholder.Length)
            cmsHex = cmsHex.Substring(0, placeholder.Length); // truncate if too large

        // Pad with zeros
        cmsHex = cmsHex.PadRight(placeholder.Length, '0');

        // Replace placeholder
        var result = (byte[])pdfWithPlaceholder.Clone();
        var cmsBytes = Encoding.ASCII.GetBytes(cmsHex);
        Array.Copy(cmsBytes, 0, result, placeholderIdx, cmsBytes.Length);

        return result;
    }

    /// <summary>
    /// Signing appends dictionary strings in plaintext; a compliant reader of
    /// an encrypted document decrypts ALL strings, turning them into garbage.
    /// </summary>
    private static void ThrowIfEncrypted(byte[] pdfBytes)
    {
        if (Encoding.ASCII.GetString(pdfBytes).IndexOf("/Encrypt <<", StringComparison.Ordinal) >= 0)
            throw new NotSupportedException(
                "Signing an encrypted PDF is not supported — sign first, then note that " +
                "encrypting afterwards invalidates the signature; use one or the other.");
    }

    private static int FindNextObjNumber(byte[] pdf)
    {
        var text = Encoding.ASCII.GetString(pdf);
        int maxObj = 0;
        int idx = 0;
        while (idx < text.Length)
        {
            idx = text.IndexOf(" 0 obj", idx, StringComparison.Ordinal);
            if (idx < 0) break;

            // Read back to find the object number
            int numEnd = idx;
            int numStart = numEnd - 1;
            while (numStart >= 0 && text[numStart] >= '0' && text[numStart] <= '9')
                numStart--;
            numStart++;

            if (numStart < numEnd && int.TryParse(text.Substring(numStart, numEnd - numStart), out int objNum))
            {
                if (objNum > maxObj) maxObj = objNum;
            }
            idx += 6;
        }
        return maxObj;
    }

    private static string EscapePdf(string text)
        => text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
