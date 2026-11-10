using System;
using System.Globalization;
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

    private static int[] ByteRange(string pdfText)
    {
        var match = Regex.Match(pdfText, @"/ByteRange \[(\d+) (\d+) (\d+) (\d+)\]");
        match.Success.Should().BeTrue("the ByteRange placeholder must be replaced with real offsets");
        var range = new int[4];
        for (int i = 0; i < range.Length; i++)
            range[i] = int.Parse(match.Groups[i + 1].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        return range;
    }

    /// <summary>
    /// The CMS bytes inside /Contents, cut at the length its DER header states: the field is zero-padded to a
    /// fixed width, and trimming trailing zero hex digits instead also ate a real trailing 0x00 signature byte.
    /// </summary>
    private static byte[] EmbeddedCms(string pdfText)
    {
        var match = Regex.Match(pdfText, @"/Contents <([0-9A-Fa-f]+)>");
        match.Success.Should().BeTrue("the signature dictionary must carry its CMS as hex in /Contents");
        var hex = match.Groups[1].Value;
        var paddedField = new byte[hex.Length / 2];
        for (int i = 0; i < paddedField.Length; i++)
            paddedField[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

        var cms = new byte[DerTlvReader.ReadTotalLength(paddedField)];
        Array.Copy(paddedField, cms, cms.Length);
        return cms;
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

        var text = Encoding.ASCII.GetString(signed);
        text.Should().Contain("/SubFilter /adbe.pkcs7.detached");
        var range = ByteRange(text);
        int firstStart = range[0], firstLength = range[1], secondStart = range[2], secondLength = range[3];
        firstStart.Should().Be(0);
        (secondStart + secondLength).Should().Be(signed.Length, "the ranges cover the file except the Contents hex");
        secondStart.Should().BeGreaterThan(firstLength, "the Contents gap sits between the two ranges");

        var content = new byte[firstLength + secondLength];
        Array.Copy(signed, firstStart, content, 0, firstLength);
        Array.Copy(signed, secondStart, content, firstLength, secondLength);
        var signedCms = new SignedCms(new ContentInfo(content), detached: true);
        signedCms.Decode(EmbeddedCms(text));

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
