using System;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfSignerCertificateFormatsTests
{
    private const string Password = "s3cret!";

    private static readonly byte[] Pdf = HtmlToPdf.Render("<p>Signed document</p>");

    private static (X509Certificate2 Cert, RSA Key) CreateRsaCertificate()
    {
        var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=EggPdf RSA Signer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return (request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)), rsa);
    }

    private static (X509Certificate2 Cert, ECDsa Key) CreateEcdsaCertificate()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=EggPdf ECDSA Signer", ecdsa, HashAlgorithmName.SHA256);
        return (request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)), ecdsa);
    }

    private static SignedCms VerifySignature(byte[] signed)
    {
        var text = Encoding.ASCII.GetString(signed);
        var byteRange = Regex.Match(text, @"/ByteRange \[(\d+) (\d+) (\d+) (\d+)\]");
        byteRange.Success.Should().BeTrue();
        int r1 = int.Parse(byteRange.Groups[2].Value), r2 = int.Parse(byteRange.Groups[3].Value), r3 = int.Parse(byteRange.Groups[4].Value);
        (r2 + r3).Should().Be(signed.Length);

        var hex = Regex.Match(text, @"/Contents <([0-9A-Fa-f]+)>").Groups[1].Value;
        var padded = new byte[hex.Length / 2];
        for (int i = 0; i < padded.Length; i++)
            padded[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        var cms = new byte[DerTlvReader.ReadTotalLength(padded)];
        Array.Copy(padded, cms, cms.Length);

        var content = new byte[r1 + r3];
        Array.Copy(signed, 0, content, 0, r1);
        Array.Copy(signed, r2, content, r1, r3);
        var signedCms = new SignedCms(new ContentInfo(content), detached: true);
        signedCms.Decode(cms);
        signedCms.CheckSignature(verifySignatureOnly: true);
        return signedCms;
    }

    [Fact]
    public void Sign_PfxBytesAndPassword_ProducesVerifiableSignature()
    {
        var (cert, key) = CreateRsaCertificate();
        using (cert) using (key)
        {
            var pfx = cert.Export(X509ContentType.Pkcs12, Password);

            var signed = PdfSigner.Sign(Pdf, pfx, Password, new PdfSigner.SignOptions { Reason = "Approval" });

            VerifySignature(signed).SignerInfos[0].Certificate!.Subject.Should().Be("CN=EggPdf RSA Signer");
        }
    }

    [Fact]
    public void Sign_PfxWrongPassword_Throws()
    {
        var (cert, key) = CreateRsaCertificate();
        using (cert) using (key)
        {
            var pfx = cert.Export(X509ContentType.Pkcs12, Password);

            Action act = () => PdfSigner.Sign(Pdf, pfx, "wrong");

            act.Should().Throw<CryptographicException>();
        }
    }

    [Fact]
    public void SignWithPem_Pkcs8RsaKey_ProducesVerifiableSignature()
    {
        var (cert, key) = CreateRsaCertificate();
        using (cert) using (key)
        {
            var signed = PdfSigner.SignWithPem(Pdf, cert.ExportCertificatePem(), key.ExportPkcs8PrivateKeyPem());

            VerifySignature(signed);
        }
    }

    [Fact]
    public void SignWithPem_Pkcs1RsaKey_ProducesVerifiableSignature()
    {
        var (cert, key) = CreateRsaCertificate();
        using (cert) using (key)
        {
            var signed = PdfSigner.SignWithPem(Pdf, cert.ExportCertificatePem(), key.ExportRSAPrivateKeyPem());

            VerifySignature(signed);
        }
    }

    [Fact]
    public void SignWithPem_EncryptedKeyAndPassword_ProducesVerifiableSignature()
    {
        var (cert, key) = CreateRsaCertificate();
        using (cert) using (key)
        {
            var encryptedKey = key.ExportEncryptedPkcs8PrivateKeyPem(Password,
                new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 100_000));

            var signed = PdfSigner.SignWithPem(Pdf, cert.ExportCertificatePem(), encryptedKey, Password);

            VerifySignature(signed);
        }
    }

    [Fact]
    public void SignWithPem_KeyNotMatchingCertificate_Throws()
    {
        var (cert, key) = CreateRsaCertificate();
        using var otherKey = RSA.Create(2048);
        using (cert) using (key)
        {
            Action act = () => PdfSigner.SignWithPem(Pdf, cert.ExportCertificatePem(), otherKey.ExportPkcs8PrivateKeyPem());

            act.Should().Throw<CryptographicException>();
        }
    }

    [Fact]
    public void Sign_EcdsaCertificate_ProducesVerifiableSignature()
    {
        var (cert, key) = CreateEcdsaCertificate();
        using (cert) using (key)
        {
            var pfx = cert.Export(X509ContentType.Pkcs12, Password);

            var signed = PdfSigner.Sign(Pdf, pfx, Password);

            VerifySignature(signed).SignerInfos[0].SignatureAlgorithm.Value.Should().Be("1.2.840.10045.4.3.2");
        }
    }

    [Fact]
    public void SignWithPem_EcPrivateKey_ProducesVerifiableSignature()
    {
        var (cert, key) = CreateEcdsaCertificate();
        using (cert) using (key)
        {
            var signed = PdfSigner.SignWithPem(Pdf, cert.ExportCertificatePem(), key.ExportECPrivateKeyPem());

            VerifySignature(signed);
        }
    }

    [Fact]
    public void FromPem_ReturnsCertificateThatBehavesLikeAPfx()
    {
        var (cert, key) = CreateRsaCertificate();
        using (cert) using (key)
        {
            using var loaded = PdfSigningCertificate.FromPem(cert.ExportCertificatePem(), key.ExportPkcs8PrivateKeyPem());

            loaded.HasPrivateKey.Should().BeTrue();
            loaded.Thumbprint.Should().Be(cert.Thumbprint);
            using var loadedKey = loaded.GetRSAPrivateKey()!;
            var data = Encoding.ASCII.GetBytes("payload");
            var signature = loadedKey.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            key.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).Should().BeTrue();
        }
    }

    [Fact]
    public void FromPfx_NullBytes_Throws()
    {
        Action act = () => PdfSigningCertificate.FromPfx(null!, null);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Sign_NullCertificateThroughOriginalOverload_StillBindsAndThrows()
    {
        Action twoArgs = () => PdfSigner.Sign(Pdf, null!);
        Action threeArgs = () => PdfSigner.Sign(Pdf, null!, null);

        twoArgs.Should().Throw<ArgumentNullException>().WithParameterName("certificate");
        threeArgs.Should().Throw<ArgumentNullException>().WithParameterName("certificate");
    }

    [Fact]
    public void SignWithCmsData_CmsLargerThanPlaceholder_Throws()
    {
        var withPlaceholder = PdfSigner.AddSignaturePlaceholder(Pdf);

        Action act = () => PdfSigner.SignWithCmsData(withPlaceholder, new byte[9000]);

        act.Should().Throw<InvalidOperationException>();
    }
}
