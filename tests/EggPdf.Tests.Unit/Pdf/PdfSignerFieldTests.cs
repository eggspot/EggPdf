using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EggPdf.Pdf;
using EggPdf.Tests.Unit.Pdf.Reading;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfSignerFieldTests
{
    private static readonly Encoding Latin1 = Encoding.GetEncoding("ISO-8859-1");

    private static X509Certificate2 Certificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=EggPdf Field Signer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
#if NET9_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
#else
        return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
#endif
    }

    private static byte[] TwoPages(string catalogExtra = "") => new TestPdfBuilder()
        .Add(1, $"<< /Type /Catalog /Pages 2 0 R {catalogExtra} >>")
        .Add(2, "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 /MediaBox [0 0 600 800] >>")
        .Add(3, "<< /Type /Page /Parent 2 0 R >>")
        .Add(4, "<< /Type /Page /Parent 2 0 R /Rotate 90 >>")
        .BuildClassic(root: 1);

    private sealed class SignatureField
    {
        public SignatureField(PdfDictionary field, PdfDictionary value) { Field = field; Value = value; }
        public PdfDictionary Field { get; }
        public PdfDictionary Value { get; }
    }

    private static List<SignatureField> SignatureFields(byte[] pdf, out PdfDictionary acroForm, out PdfObjectStore store)
    {
        var reader = PdfReader.Open(pdf);
        store = reader.Store;
        store.WasRepaired.Should().BeFalse("the update must chain a valid cross-reference");
        acroForm = (PdfDictionary)store.Resolve(store.FindCatalog()!.Get("AcroForm"))!;
        var fields = new List<SignatureField>();
        foreach (var item in ((PdfArray)store.Resolve(acroForm.Get("Fields"))!).Items)
        {
            var field = (PdfDictionary)store.Resolve(item)!;
            if (store.Resolve(field.Get("FT")) is PdfName { Value: "Sig" })
                fields.Add(new SignatureField(field, (PdfDictionary)store.Resolve(field.Get("V"))!));
        }
        return fields;
    }

    private static long[] ByteRange(PdfDictionary signature)
    {
        var items = ((PdfArray)signature.Get("ByteRange")!).Items;
        var range = new long[4];
        for (int i = 0; i < 4; i++) range[i] = (long)((PdfNumber)items[i]).Value;
        return range;
    }

    private static void ShouldVerify(byte[] pdf, PdfDictionary signature)
    {
        var range = ByteRange(signature);
        range[0].Should().Be(0);
        (range[2] + range[3]).Should().BeLessOrEqualTo(pdf.Length);
        pdf[range[1]].Should().Be((byte)'<');
        pdf[range[2] - 1].Should().Be((byte)'>');
        var hex = Latin1.GetString(pdf, (int)range[1] + 1, (int)(range[2] - range[1] - 2));
        var all = new byte[hex.Length / 2];
        for (int i = 0; i < all.Length; i++) all[i] = byte.Parse(hex.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var cms = new byte[DerTlvReader.ReadTotalLength(all)];
        Array.Copy(all, cms, cms.Length);
        var content = new byte[range[1] + range[3]];
        Array.Copy(pdf, 0, content, 0, range[1]);
        Array.Copy(pdf, range[2], content, range[1], range[3]);

        var signed = new SignedCms(new ContentInfo(content), detached: true);
        signed.Decode(cms);
        signed.CheckSignature(verifySignatureOnly: true);
    }

    [Fact]
    public void Sign_WritesASignatureFieldTheDocumentReferences()
    {
        var original = TwoPages();
        using var certificate = Certificate();

        var signed = PdfSigner.Sign(original, certificate, new PdfSigner.SignOptions { Reason = "Approval" });

        signed.AsSpan(0, original.Length).SequenceEqual(original).Should().BeTrue("signing appends an incremental update");
        var fields = SignatureFields(signed, out var acroForm, out var store);
        fields.Should().HaveCount(1);
        ((PdfNumber)acroForm.Get("SigFlags")!).Value.Should().Be(3);
        var field = fields[0].Field;
        ((PdfName)field.Get("Subtype")!).Value.Should().Be("Widget");
        field.Get("T").Should().BeOfType<PdfString>();
        ((PdfName)fields[0].Value.Get("SubFilter")!).Value.Should().Be("adbe.pkcs7.detached");
        Latin1.GetString(((PdfString)fields[0].Value.Get("Reason")!).Bytes).Should().Be("Approval");
        var firstPage = PdfReader.Open(signed).GetPageNode(1);
        var annotations = (PdfArray)store.Resolve(firstPage.Dictionary.Get("Annots"))!;
        annotations.Items.Should().ContainSingle(item => ReferenceEquals(store.Resolve(item), field));
        ((PdfReference)field.Get("P")!).ObjectNumber.Should().Be(firstPage.Reference!.ObjectNumber);
        (ByteRange(fields[0].Value)[2] + ByteRange(fields[0].Value)[3]).Should().Be(signed.Length, "the newest signature covers the whole file but its /Contents value");
        ShouldVerify(signed, fields[0].Value);
    }

    [Fact]
    public void Sign_Twice_KeepsBothSignaturesValid()
    {
        using var certificate = Certificate();

        var once = PdfSigner.Sign(TwoPages(), certificate);
        var twice = PdfSigner.Sign(once, certificate);

        var fields = SignatureFields(twice, out _, out _);
        fields.Should().HaveCount(2);
        Latin1.GetString(((PdfString)fields[0].Field.Get("T")!).Bytes)
            .Should().NotBe(Latin1.GetString(((PdfString)fields[1].Field.Get("T")!).Bytes));
        foreach (var field in fields) ShouldVerify(twice, field.Value);
        (ByteRange(fields[0].Value)[2] + ByteRange(fields[0].Value)[3]).Should().Be(once.Length, "the first signature still covers exactly the bytes it signed");
    }

    [Fact]
    public void Sign_ExistingFormFields_AreKept()
    {
        var original = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [5 0 R] /DA (/Helv 0 Tf 0 g) >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 600 800] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [5 0 R] >>")
            .Add(5, "<< /FT /Tx /T (Name) /Subtype /Widget /Rect [10 10 100 30] /P 3 0 R >>")
            .BuildClassic(root: 1);
        using var certificate = Certificate();

        var signed = PdfSigner.Sign(original, certificate);

        SignatureFields(signed, out var acroForm, out var store).Should().HaveCount(1);
        var fields = ((PdfArray)store.Resolve(acroForm.Get("Fields"))!).Items;
        fields.Should().HaveCount(2);
        ((PdfReference)fields[0]).ObjectNumber.Should().Be(5);
        acroForm.Get("DA").Should().NotBeNull();
        var annotations = (PdfArray)store.Resolve(PdfReader.Open(signed).GetPageNode(1).Dictionary.Get("Annots"))!;
        annotations.Items.Should().HaveCount(2);
    }

    [Fact]
    public void Sign_XrefStreamInput_AppendsAnXrefStreamUpdate()
    {
        var original = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 600 800] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>", inObjectStream: true)
            .BuildWithXrefStream(root: 1, pngUpPredictor: true);
        using var certificate = Certificate();

        var signed = PdfSigner.Sign(original, certificate);

        Latin1.GetString(signed, original.Length, signed.Length - original.Length).Should().Contain("/Type /XRef");
        var fields = SignatureFields(signed, out _, out _);
        ShouldVerify(signed, fields.Should().ContainSingle().Subject.Value);
    }

    [Fact]
    public void Sign_CertifiedDocumentForbiddingChanges_ThrowsNotSupported()
    {
        var original = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /Perms << /DocMDP 4 0 R >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 600 800] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>")
            .Add(4, "<< /Type /Sig /Reference [<< /Type /SigRef /TransformMethod /DocMDP /TransformParams << /P 1 /V /1.2 >> >>] >>")
            .BuildClassic(root: 1);
        using var certificate = Certificate();

        Action act = () => PdfSigner.Sign(original, certificate);

        act.Should().Throw<NotSupportedException>().WithMessage("*certif*");
    }

    [Fact]
    public void Sign_CertifiedDocumentAllowingSignatures_Signs()
    {
        var original = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /Perms << /DocMDP 4 0 R >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 600 800] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>")
            .Add(4, "<< /Type /Sig /Reference [<< /Type /SigRef /TransformMethod /DocMDP /TransformParams << /P 2 /V /1.2 >> >>] >>")
            .BuildClassic(root: 1);
        using var certificate = Certificate();

        var signed = PdfSigner.Sign(original, certificate);

        SignatureFields(signed, out _, out _).Should().ContainSingle();
    }

    [Fact]
    public void Sign_DamagedCrossReference_ThrowsPdfFormatException()
    {
        var text = Latin1.GetString(TwoPages());
        var damaged = Latin1.GetBytes(text.Substring(0, text.LastIndexOf("startxref", StringComparison.Ordinal)) + "startxref\n999999\n%%EOF\n");
        using var certificate = Certificate();

        Action act = () => PdfSigner.Sign(damaged, certificate);

        act.Should().Throw<PdfFormatException>();
    }

    [Fact]
    public void AddSignaturePlaceholder_ThenSignWithCmsData_ProducesAValidSignature()
    {
        using var certificate = Certificate();
        var prepared = PdfSigner.AddSignaturePlaceholder(TwoPages(), new PdfSigner.SignOptions { Name = "External" });
        var signature = SignatureFields(prepared, out _, out _).Should().ContainSingle().Subject.Value;
        var range = ByteRange(signature);
        byte[] digest;
        using (var sha = SHA256.Create())
        {
            sha.TransformBlock(prepared, 0, (int)range[1], null, 0);
            sha.TransformFinalBlock(prepared, (int)range[2], (int)range[3]);
            digest = sha.Hash!;
        }

        var signed = PdfSigner.SignWithCmsData(prepared, CmsSignedDataBuilder.BuildDetached(digest, certificate, DateTime.UtcNow));

        signed.Length.Should().Be(prepared.Length);
        ShouldVerify(signed, SignatureFields(signed, out _, out _).Should().ContainSingle().Subject.Value);
    }

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==");

    private static double[] Numbers(PdfObjectStore store, PdfObject? value)
    {
        var items = ((PdfArray)store.Resolve(value)!).Items;
        var numbers = new double[items.Count];
        for (int i = 0; i < numbers.Length; i++) numbers[i] = ((PdfNumber)store.Resolve(items[i])!).Value;
        return numbers;
    }

    [Theory]
    [InlineData(1, new[] { 50.0, 700, 170, 740 }, new[] { 1.0, 0, 0, 1, 0, 0 }, 0)]
    [InlineData(2, new[] { 60.0, 50, 100, 170 }, new[] { 0.0, 1, -1, 0, 0, 0 }, 90)]
    public void Sign_VisibleAppearance_ShowsTheImageInAWidgetOnThePage(int page, double[] rectangle, double[] matrix, int rotation)
    {
        using var certificate = Certificate();
        var options = new PdfSigner.SignOptions { Appearance = new PdfSigner.SignatureAppearance(page, 50, 60, 120, 40, Png) };

        var signed = PdfSigner.Sign(TwoPages(), certificate, options);

        var field = SignatureFields(signed, out _, out var store).Should().ContainSingle().Subject;
        Numbers(store, field.Field.Get("Rect")).Should().Equal(rectangle);
        ((PdfNumber)field.Field.Get("F")!).Value.Should().Be(4);
        var form = (PdfStream)store.Resolve(((PdfDictionary)field.Field.Get("AP")!).Get("N"))!;
        Numbers(store, form.Dictionary.Get("BBox")).Should().Equal(0, 0, 120, 40);
        Numbers(store, form.Dictionary.Get("Matrix")).Should().Equal(matrix);
        Latin1.GetString(PdfStreamDecoder.Decode(form)).Should().Contain("/Img Do");
        if (rotation == 0) field.Field.Get("MK").Should().BeNull();
        else ((PdfNumber)((PdfDictionary)field.Field.Get("MK")!).Get("R")!).Value.Should().Be(rotation);
        var annotations = (PdfArray)store.Resolve(PdfReader.Open(signed).GetPageNode(page).Dictionary.Get("Annots"))!;
        annotations.Items.Should().ContainSingle();
        ShouldVerify(signed, field.Value);
    }

    [Fact]
    public void Sign_InvisibleSignature_HasAnEmptyAppearanceOnPageOne()
    {
        using var certificate = Certificate();

        var signed = PdfSigner.Sign(TwoPages(), certificate);

        var field = SignatureFields(signed, out _, out var store).Should().ContainSingle().Subject.Field;
        Numbers(store, field.Get("Rect")).Should().Equal(0, 0, 0, 0);
        var form = (PdfStream)store.Resolve(((PdfDictionary)field.Get("AP")!).Get("N"))!;
        Numbers(store, form.Dictionary.Get("BBox")).Should().Equal(0, 0, 0, 0);
    }

    [Fact]
    public void Sign_ExistingEmptySignatureField_SignsIntoIt()
    {
        var original = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [5 0 R] >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 600 800] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Annots [5 0 R] >>")
            .Add(5, "<< /FT /Sig /T (Approver) /Type /Annot /Subtype /Widget /Rect [100 100 300 150] /P 3 0 R /F 4 >>")
            .BuildClassic(root: 1);
        using var certificate = Certificate();

        var signed = PdfSigner.Sign(original, certificate, new PdfSigner.SignOptions { FieldName = "Approver" });

        var field = SignatureFields(signed, out var acroForm, out var store).Should().ContainSingle().Subject;
        ((PdfArray)store.Resolve(acroForm.Get("Fields"))!).Items.Should().ContainSingle();
        Numbers(store, field.Field.Get("Rect")).Should().Equal(100, 100, 300, 150);
        ShouldVerify(signed, field.Value);
    }

    [Fact]
    public void Sign_FieldNameOfASignedOrNonSignatureField_Throws()
    {
        var original = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [5 0 R] >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 600 800] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>")
            .Add(5, "<< /FT /Tx /T (Name) >>")
            .BuildClassic(root: 1);
        using var certificate = Certificate();
        var signedOnce = PdfSigner.Sign(TwoPages(), certificate, new PdfSigner.SignOptions { FieldName = "Approver" });

        Action notSignature = () => PdfSigner.Sign(original, certificate, new PdfSigner.SignOptions { FieldName = "Name" });
        Action alreadySigned = () => PdfSigner.Sign(signedOnce, certificate, new PdfSigner.SignOptions { FieldName = "Approver" });

        notSignature.Should().Throw<ArgumentException>();
        alreadySigned.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Sign_InvalidAppearance_Throws()
    {
        using var certificate = Certificate();
        PdfSigner.SignOptions With(PdfSigner.SignatureAppearance appearance) => new() { Appearance = appearance };

        Action page = () => PdfSigner.Sign(TwoPages(), certificate, With(new PdfSigner.SignatureAppearance(3, 0, 0, 10, 10, Png)));
        Action box = () => PdfSigner.Sign(TwoPages(), certificate, With(new PdfSigner.SignatureAppearance(1, 0, 0, 0, 10, Png)));
        Action image = () => PdfSigner.Sign(TwoPages(), certificate, With(new PdfSigner.SignatureAppearance(1, 0, 0, 10, 10, new byte[] { 1, 2, 3 })));

        page.Should().Throw<ArgumentOutOfRangeException>();
        box.Should().Throw<ArgumentOutOfRangeException>();
        image.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Sign_UnicodeNameAndReason_AreWrittenAsUnicodeText()
    {
        using var certificate = Certificate();

        var signed = PdfSigner.Sign(TwoPages(), certificate, new PdfSigner.SignOptions { Name = "Nguyễn Văn A", Reason = "Đồng ý" });

        var signature = SignatureFields(signed, out _, out _).Should().ContainSingle().Subject.Value;
        var name = ((PdfString)signature.Get("Name")!).Bytes;
        Encoding.BigEndianUnicode.GetString(name, 2, name.Length - 2).Should().Be("Nguyễn Văn A");
        var reason = ((PdfString)signature.Get("Reason")!).Bytes;
        Encoding.BigEndianUnicode.GetString(reason, 2, reason.Length - 2).Should().Be("Đồng ý");
    }

    [Fact]
    public void Sign_DocumentLockedBySignature_ThrowsNotSupported()
    {
        var original = new TestPdfBuilder()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [5 0 R] /SigFlags 3 >> >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 600 800] >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R >>")
            .Add(5, "<< /FT /Sig /T (Final) /V 6 0 R /Lock << /Type /SigFieldLock /Action /All /P 1 >> >>")
            .Add(6, "<< /Type /Sig >>")
            .BuildClassic(root: 1);
        using var certificate = Certificate();

        Action act = () => PdfSigner.Sign(original, certificate);

        act.Should().Throw<NotSupportedException>().WithMessage("*lock*");
    }

    [Fact]
    public void Sign_LargeStreamInput_HoldsOnlyTheUpdateInMemory()
    {
        var head = Latin1.GetBytes("%PDF-1.7\n1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n3 0 obj\n<< /Length 209715200 >>\nstream\n");
        var tail = new StringBuilder("\nendstream\nendobj\n");
        long fillerEnd = head.Length + 209_715_200L;
        long pages = fillerEnd + tail.Length;
        tail.Append("2 0 obj\n<< /Type /Pages /Kids [4 0 R] /Count 1 >>\nendobj\n");
        long page = fillerEnd + tail.Length;
        tail.Append("4 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>\nendobj\n");
        long xref = fillerEnd + tail.Length;
        tail.Append("xref\n0 5\n0000000000 65535 f \n0000000009 00000 n \n")
            .Append(pages.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n")
            .Append(((long)head.Length - Latin1.GetBytes("3 0 obj\n<< /Length 209715200 >>\nstream\n").Length).ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n")
            .Append(page.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n")
            .Append("trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n").Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");
        var file = new PdfReaderLargeFileTests.VirtualFile(head, 209_715_200L, Latin1.GetBytes(tail.ToString()));
        var output = new PdfReaderLargeFileTests.TailCapture(file.Length);
        using var certificate = Certificate();

        long before = GC.GetAllocatedBytesForCurrentThread();
        PdfSigner.Sign(file, output, certificate);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Latin1.GetString(output.Kept).Should().Contain("/FT /Sig");
        allocated.Should().BeLessThan(8 * 1024 * 1024);
    }

    [Fact]
    public void Sign_FromStreamToStream_ProducesAValidSignatureWithoutLoadingTheInput()
    {
        var original = TwoPages();
        using var certificate = Certificate();
        var output = new MemoryStream();

        PdfSigner.Sign(new MemoryStream(original, writable: false), output, certificate);

        var signed = output.ToArray();
        signed.AsSpan(0, original.Length).SequenceEqual(original).Should().BeTrue();
        ShouldVerify(signed, SignatureFields(signed, out _, out _).Should().ContainSingle().Subject.Value);
    }
}
