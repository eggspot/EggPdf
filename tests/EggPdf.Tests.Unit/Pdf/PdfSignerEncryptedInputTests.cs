using System;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>
/// Signing appends plaintext dictionary strings, which a compliant reader of
/// an ENCRYPTED document would RC4-"decrypt" into garbage. The signer must
/// refuse encrypted inputs instead of silently corrupting them.
/// </summary>
public class PdfSignerEncryptedInputTests
{
    private static byte[] EncryptedPdf()
    {
        var doc = new PdfDocument { Encryption = new PdfEncryption { OwnerPassword = "owner" } };
        var page = doc.AddPage(595, 842);
        page.AddText("secret", 50, 700, "Helvetica", 12);
        return doc.ToByteArray();
    }

    [Fact]
    public void Sign_EncryptedInput_Throws()
    {
        using var cert = TestCertificate();
        var act = () => PdfSigner.Sign(EncryptedPdf(), cert);
        act.Should().Throw<NotSupportedException>(
            "appended signature strings would be mis-decrypted by the security handler");
    }

    [Fact]
    public void AddSignaturePlaceholder_EncryptedInput_Throws()
    {
        var act = () => PdfSigner.AddSignaturePlaceholder(EncryptedPdf());
        act.Should().Throw<NotSupportedException>();
    }

    private static System.Security.Cryptography.X509Certificates.X509Certificate2 TestCertificate()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=EggPdf Test", rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        return req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
