using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// Builds a detached CMS/PKCS#7 SignedData structure (RFC 5652) for PDF
/// signatures (adbe.pkcs7.detached) using only in-box crypto primitives —
/// no System.Security.Cryptography.Pkcs package. SHA-256 digest, RSA
/// PKCS#1 v1.5 signature, signed attributes (content-type, message-digest,
/// signing-time).
/// </summary>
internal static class CmsSignedDataBuilder
{
    private const string OidData = "1.2.840.113549.1.7.1";
    private const string OidSignedData = "1.2.840.113549.1.7.2";
    private const string OidSha256 = "2.16.840.1.101.3.4.2.1";
    private const string OidRsa = "1.2.840.113549.1.1.1";
    private const string OidContentType = "1.2.840.113549.1.9.3";
    private const string OidMessageDigest = "1.2.840.113549.1.9.4";
    private const string OidSigningTime = "1.2.840.113549.1.9.5";

    /// <summary>
    /// Build a detached SignedData over content whose SHA-256 digest is given.
    /// </summary>
    public static byte[] BuildDetached(byte[] contentDigestSha256, X509Certificate2 certificate, DateTime signingTimeUtc)
    {
        var rsa = certificate.GetRSAPrivateKey();
        if (rsa == null)
            throw new InvalidOperationException("The certificate has no RSA private key.");

        using (rsa)
        {
            // Signed attributes (DER SET OF, sorted by encoding)
            var attrContentType = Sequence(Oid(OidContentType), SetOf(Oid(OidData)));
            var attrSigningTime = Sequence(Oid(OidSigningTime), SetOf(UtcTime(signingTimeUtc)));
            var attrMessageDigest = Sequence(Oid(OidMessageDigest), SetOf(OctetString(contentDigestSha256)));
            var signedAttrsSet = SetOfSorted(attrContentType, attrSigningTime, attrMessageDigest);

            // The signature covers the DER SET OF (tag 0x31) encoding of the attributes
            byte[] attrsDigest;
            using (var sha = SHA256.Create())
                attrsDigest = sha.ComputeHash(signedAttrsSet);
            var signature = rsa.SignHash(attrsDigest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            var digestAlg = Sequence(Oid(OidSha256), Null());
            var sigAlg = Sequence(Oid(OidRsa), Null());

            var issuerAndSerial = Sequence(
                Raw(certificate.IssuerName.RawData),
                IntegerFromSerial(certificate.GetSerialNumber()));

            var signerInfo = Sequence(
                Integer(1),
                issuerAndSerial,
                digestAlg,
                Retag(signedAttrsSet, 0xA0), // [0] IMPLICIT SignedAttributes
                sigAlg,
                OctetString(signature));

            var signedData = Sequence(
                Integer(1),
                SetOf(digestAlg),
                Sequence(Oid(OidData)),                      // detached: no eContent
                Retag(SetOf(Raw(certificate.RawData)), 0xA0), // [0] IMPLICIT CertificateSet
                SetOf(signerInfo));

            // ContentInfo { signedData OID, [0] EXPLICIT SignedData }
            return Sequence(Oid(OidSignedData), Tlv(0xA0, signedData));
        }
    }

    // ===== Minimal DER encoding helpers =====

    private static byte[] Tlv(byte tag, byte[] content)
    {
        byte[] lenBytes;
        int len = content.Length;
        if (len < 0x80)
            lenBytes = new[] { (byte)len };
        else if (len <= 0xFF)
            lenBytes = new byte[] { 0x81, (byte)len };
        else if (len <= 0xFFFF)
            lenBytes = new byte[] { 0x82, (byte)(len >> 8), (byte)len };
        else
            lenBytes = new byte[] { 0x83, (byte)(len >> 16), (byte)(len >> 8), (byte)len };

        var result = new byte[1 + lenBytes.Length + len];
        result[0] = tag;
        Array.Copy(lenBytes, 0, result, 1, lenBytes.Length);
        Array.Copy(content, 0, result, 1 + lenBytes.Length, len);
        return result;
    }

    private static byte[] Concat(byte[][] parts)
    {
        int total = 0;
        foreach (var p in parts) total += p.Length;
        var result = new byte[total];
        int pos = 0;
        foreach (var p in parts)
        {
            Array.Copy(p, 0, result, pos, p.Length);
            pos += p.Length;
        }
        return result;
    }

    private static byte[] Sequence(params byte[][] parts) => Tlv(0x30, Concat(parts));

    private static byte[] SetOf(params byte[][] parts) => Tlv(0x31, Concat(parts));

    /// <summary>DER SET OF requires elements sorted by their encoded bytes.</summary>
    private static byte[] SetOfSorted(params byte[][] parts)
    {
        var list = new List<byte[]>(parts);
        list.Sort(CompareDer);
        return SetOf(list.ToArray());
    }

    private static int CompareDer(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
        {
            int c = a[i].CompareTo(b[i]);
            if (c != 0) return c;
        }
        return a.Length.CompareTo(b.Length);
    }

    private static byte[] Oid(string dotted)
    {
        var parts = dotted.Split('.');
        var body = new List<byte>();
        body.Add((byte)(int.Parse(parts[0]) * 40 + int.Parse(parts[1])));
        for (int i = 2; i < parts.Length; i++)
        {
            long v = long.Parse(parts[i]);
            var chunk = new List<byte>();
            do
            {
                chunk.Insert(0, (byte)(v & 0x7F));
                v >>= 7;
            } while (v > 0);
            for (int c = 0; c < chunk.Count - 1; c++)
                chunk[c] |= 0x80;
            body.AddRange(chunk);
        }
        return Tlv(0x06, body.ToArray());
    }

    private static byte[] Integer(int value)
    {
        if (value >= 0 && value < 0x80)
            return new byte[] { 0x02, 0x01, (byte)value };
        var bytes = new List<byte>();
        uint v = (uint)value;
        while (v > 0) { bytes.Insert(0, (byte)(v & 0xFF)); v >>= 8; }
        if (bytes.Count == 0) bytes.Add(0);
        if ((bytes[0] & 0x80) != 0) bytes.Insert(0, 0);
        return Tlv(0x02, bytes.ToArray());
    }

    /// <summary>X509Certificate2.GetSerialNumber() returns little-endian bytes.</summary>
    private static byte[] IntegerFromSerial(byte[] littleEndianSerial)
    {
        var big = (byte[])littleEndianSerial.Clone();
        Array.Reverse(big);
        int skip = 0;
        while (skip < big.Length - 1 && big[skip] == 0 && (big[skip + 1] & 0x80) == 0)
            skip++;
        var trimmed = new byte[big.Length - skip];
        Array.Copy(big, skip, trimmed, 0, trimmed.Length);
        if ((trimmed[0] & 0x80) != 0)
        {
            var padded = new byte[trimmed.Length + 1];
            Array.Copy(trimmed, 0, padded, 1, trimmed.Length);
            trimmed = padded;
        }
        return Tlv(0x02, trimmed);
    }

    private static byte[] OctetString(byte[] data) => Tlv(0x04, data);

    private static byte[] Null() => new byte[] { 0x05, 0x00 };

    private static byte[] UtcTime(DateTime utc) =>
        Tlv(0x17, Encoding.ASCII.GetBytes(utc.ToString("yyMMddHHmmss") + "Z"));

    private static byte[] Raw(byte[] der) => der;

    /// <summary>Replace an encoding's tag byte (IMPLICIT context tagging).</summary>
    private static byte[] Retag(byte[] tlv, byte newTag)
    {
        var result = (byte[])tlv.Clone();
        result[0] = newTag;
        return result;
    }
}
