using System;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>
/// One-call PKI signing: PdfSigner.Sign embeds a detached CMS/PKCS#7 signature
/// (adbe.pkcs7.detached) with a correct /ByteRange, verifiable by standard
/// CMS validators.
/// </summary>
public class PdfSignerTests
{
    private static X509Certificate2 CreateSelfSignedCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=EggPdf Test Signer, O=VCRRM",
            rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);
#else
        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
#endif
    }

    [Fact]
    public async Task Sign_ProducesVerifiableDetachedCms()
    {
        var pdf = await HtmlToPdf.RenderAsync("<html><body><p>Signed certificate document</p></body></html>");
        using var cert = CreateSelfSignedCert();

        var signed = PdfSigner.Sign(pdf, cert, new PdfSigner.SignOptions
        {
            Name = "EggPdf Test Signer",
            Reason = "Approval",
            Location = "Hanoi"
        });

        signed.Should().NotBeNull();
        var text = Encoding.ASCII.GetString(signed);

        // 1. Structure: signature dictionary with real ByteRange
        text.Should().Contain("/SubFilter /adbe.pkcs7.detached");
        var brMatch = Regex.Match(text, @"/ByteRange \[(\d+) (\d+) (\d+) (\d+)\]");
        brMatch.Success.Should().BeTrue("ByteRange must contain real numbers");
        int r0 = int.Parse(brMatch.Groups[1].Value);
        int r1 = int.Parse(brMatch.Groups[2].Value);
        int r2 = int.Parse(brMatch.Groups[3].Value);
        int r3 = int.Parse(brMatch.Groups[4].Value);
        r0.Should().Be(0);
        (r2 + r3).Should().Be(signed.Length, "ranges must cover the file except the Contents hex");
        r2.Should().BeGreaterThan(r1, "the Contents gap sits between the two ranges");

        // 2. Extract the CMS from /Contents <hex>
        int hexStart = -1;
        var cMatch = Regex.Match(text, @"/Contents <([0-9A-Fa-f0]+)>");
        cMatch.Success.Should().BeTrue();
        hexStart = cMatch.Groups[1].Index;
        var hex = cMatch.Groups[1].Value.TrimEnd('0');
        if (hex.Length % 2 == 1) hex += "0";
        var cms = new byte[hex.Length / 2];
        for (int i = 0; i < cms.Length; i++)
            cms[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

        // 3. Verify the signature over the ByteRange content with a real validator
        var content = new byte[r1 + r3];
        Array.Copy(signed, r0, content, 0, r1);
        Array.Copy(signed, r2, content, r1, r3);

        var signedCms = new SignedCms(new ContentInfo(content), detached: true);
        signedCms.Decode(cms);
        var act = () => signedCms.CheckSignature(verifySignatureOnly: true);
        act.Should().NotThrow("the embedded CMS must be cryptographically valid over the ByteRange");

        signedCms.SignerInfos.Count.Should().Be(1);
        signedCms.Certificates.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Sign_NullPdf_ReturnsEmpty()
    {
        using var cert = CreateSelfSignedCert();
        PdfSigner.Sign(null!, cert).Should().BeEmpty();
    }
}
