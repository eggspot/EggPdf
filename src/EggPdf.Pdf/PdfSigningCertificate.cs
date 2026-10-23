using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EggPdf.Pdf;

/// <summary>
/// Loads a signing certificate with its private key for <see cref="PdfSigner"/>, from a PFX/PKCS#12 file or from
/// a PEM certificate plus PEM private key (RSA or ECDSA). A PEM pair is re-exported through PKCS#12, so it
/// behaves exactly like a certificate loaded from a PFX. Load once and sign many documents; the caller owns
/// the returned certificate and must dispose it.
/// </summary>
public static class PdfSigningCertificate
{
    /// <summary>Loads a certificate and its private key from PFX/PKCS#12 bytes (<paramref name="password"/> null or empty when it has none).</summary>
    /// <exception cref="CryptographicException">The bytes aren't a PKCS#12 file or the password is wrong.</exception>
    public static X509Certificate2 FromPfx(byte[] pfx, string? password)
    {
        if (pfx == null) throw new ArgumentNullException(nameof(pfx));
#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(pfx, password, KeyStorageFlags);
#else
        return new X509Certificate2(pfx, password, KeyStorageFlags);
#endif
    }

    /// <summary>
    /// Loads a certificate from <paramref name="certificatePem"/> (<c>-----BEGIN CERTIFICATE-----</c>) and its private key
    /// from <paramref name="privateKeyPem"/>: <c>PRIVATE KEY</c> (PKCS#8), <c>RSA PRIVATE KEY</c>, <c>EC PRIVATE KEY</c>, or
    /// <c>ENCRYPTED PRIVATE KEY</c> with <paramref name="privateKeyPassword"/>. Not available on .NET Standard 2.0
    /// (e.g. .NET Framework): use <see cref="FromPfx"/> there.
    /// </summary>
    /// <exception cref="CryptographicException">The PEM can't be read, the password is wrong, or the key doesn't match the certificate.</exception>
    /// <exception cref="PlatformNotSupportedException">Running on the .NET Standard 2.0 build.</exception>
    public static X509Certificate2 FromPem(string certificatePem, string privateKeyPem, string? privateKeyPassword = null)
    {
        if (certificatePem == null) throw new ArgumentNullException(nameof(certificatePem));
        if (privateKeyPem == null) throw new ArgumentNullException(nameof(privateKeyPem));
#if NETSTANDARD2_0
        throw new PlatformNotSupportedException(
            "Loading a PEM certificate and key needs .NET Standard 2.1 or later; convert it to a PFX and use FromPfx.");
#else
        using var withKey = LoadPemWithKey(certificatePem, privateKeyPem, privateKeyPassword);
        var pfx = withKey.Export(X509ContentType.Pkcs12);
        try
        {
            return FromPfx(pfx, null);
        }
        finally
        {
            Array.Clear(pfx, 0, pfx.Length);
        }
#endif
    }

    private static X509KeyStorageFlags KeyStorageFlags =>
#if NETSTANDARD2_0
        X509KeyStorageFlags.DefaultKeySet;
#else
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;
#endif

#if NET5_0_OR_GREATER
    private static X509Certificate2 LoadPemWithKey(string certificatePem, string privateKeyPem, string? password) =>
        password == null
            ? X509Certificate2.CreateFromPem(certificatePem, privateKeyPem)
            : X509Certificate2.CreateFromEncryptedPem(certificatePem, privateKeyPem, password);
#elif NETSTANDARD2_1
    private static X509Certificate2 LoadPemWithKey(string certificatePem, string privateKeyPem, string? password)
    {
        using var certificate = new X509Certificate2(PemBlock(certificatePem, "CERTIFICATE"));
        var (label, keyDer) = FirstKeyBlock(privateKeyPem);
        switch (label)
        {
            case "EC PRIVATE KEY":
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportECPrivateKey(keyDer, out _);
                return certificate.CopyWithPrivateKey(ecdsa);
            }
            case "PRIVATE KEY":
            case "ENCRYPTED PRIVATE KEY":
                return IsEcCertificate(certificate)
                    ? WithEcdsaPkcs8(certificate, keyDer, label, password)
                    : WithRsaPkcs8(certificate, keyDer, label, password);
            default:
            {
                using var rsa = RSA.Create();
                rsa.ImportRSAPrivateKey(keyDer, out _);
                return certificate.CopyWithPrivateKey(rsa);
            }
        }
    }

    private const string EcPublicKeyOid = "1.2.840.10045.2.1";

    private static bool IsEcCertificate(X509Certificate2 certificate) => certificate.PublicKey.Oid.Value == EcPublicKeyOid;

    private static X509Certificate2 WithRsaPkcs8(X509Certificate2 certificate, byte[] keyDer, string label, string? password)
    {
        using var rsa = RSA.Create();
        if (label == "ENCRYPTED PRIVATE KEY")
            rsa.ImportEncryptedPkcs8PrivateKey(RequirePassword(password), keyDer, out _);
        else
            rsa.ImportPkcs8PrivateKey(keyDer, out _);
        return certificate.CopyWithPrivateKey(rsa);
    }

    private static X509Certificate2 WithEcdsaPkcs8(X509Certificate2 certificate, byte[] keyDer, string label, string? password)
    {
        using var ecdsa = ECDsa.Create();
        if (label == "ENCRYPTED PRIVATE KEY")
            ecdsa.ImportEncryptedPkcs8PrivateKey(RequirePassword(password), keyDer, out _);
        else
            ecdsa.ImportPkcs8PrivateKey(keyDer, out _);
        return certificate.CopyWithPrivateKey(ecdsa);
    }

    private static string RequirePassword(string? password) =>
        password ?? throw new CryptographicException("The private key is encrypted: pass its password.");

    private static (string Label, byte[] Der) FirstKeyBlock(string pem)
    {
        foreach (var label in new[] { "ENCRYPTED PRIVATE KEY", "RSA PRIVATE KEY", "EC PRIVATE KEY", "PRIVATE KEY" })
        {
            var der = TryPemBlock(pem, label);
            if (der != null) return (label, der);
        }
        throw new CryptographicException("No PEM private key found (expected PRIVATE KEY, RSA PRIVATE KEY, EC PRIVATE KEY or ENCRYPTED PRIVATE KEY).");
    }

    private static byte[] PemBlock(string pem, string label) =>
        TryPemBlock(pem, label) ?? throw new CryptographicException($"No PEM '{label}' block found.");

    private static byte[]? TryPemBlock(string pem, string label)
    {
        var begin = "-----BEGIN " + label + "-----";
        var end = "-----END " + label + "-----";
        int start = pem.IndexOf(begin, StringComparison.Ordinal);
        if (start < 0) return null;
        start += begin.Length;
        int stop = pem.IndexOf(end, start, StringComparison.Ordinal);
        if (stop < 0) return null;
        try
        {
            return Convert.FromBase64String(pem.Substring(start, stop - start));
        }
        catch (FormatException ex)
        {
            throw new CryptographicException($"The PEM '{label}' block isn't valid base64.", ex);
        }
    }
#endif
}
