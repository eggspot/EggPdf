using System;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

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
            "signing appends plaintext dictionary strings, which a reader of an encrypted document would RC4-decrypt " +
            "into garbage, so the signer refuses rather than silently corrupt the file");
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
