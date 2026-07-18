using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>
/// End-to-end encryption tests that act as an independent, spec-compliant
/// PDF reader: they take /O, /P, /ID from the file, derive the file key from
/// the user password per PDF 32000 Algorithm 2, verify /U per Algorithm 4/5,
/// derive per-object keys per Algorithm 1, and RC4-decrypt streams/strings.
/// A derivation bug anywhere in the library makes these fail — the same way
/// a real viewer would show garbage.
/// </summary>
public class PdfEncryptionE2ETests
{
    private const string Secret = "Top secret certificate text";

    // ---------- mini spec-compliant reader ----------

    private static readonly byte[] Pad =
    {
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4B, 0x49, 0x43, 0x28, 0x46, 0x57,
        0x44, 0x28, 0x55, 0x78, 0x65, 0x63, 0x68, 0x6E,
        0x69, 0x63, 0x61, 0x6C, 0x20, 0x49, 0x6E, 0x66
    };

    private static byte[] Md5(params byte[][] parts)
    {
        using var md5 = MD5.Create();
        using var ms = new MemoryStream();
        foreach (var p in parts) ms.Write(p, 0, p.Length);
        return md5.ComputeHash(ms.ToArray());
    }

    private static byte[] Rc4(byte[] key, byte[] data)
    {
        var s = new byte[256];
        for (int i = 0; i < 256; i++) s[i] = (byte)i;
        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }
        var result = new byte[data.Length];
        int x = 0, y = 0;
        for (int i = 0; i < data.Length; i++)
        {
            x = (x + 1) & 0xFF;
            y = (y + s[x]) & 0xFF;
            (s[x], s[y]) = (s[y], s[x]);
            result[i] = (byte)(data[i] ^ s[(s[x] + s[y]) & 0xFF]);
        }
        return result;
    }

    private static byte[] PadPassword(string password)
    {
        var pwd = Encoding.GetEncoding(28591).GetBytes(password);
        var padded = new byte[32];
        int len = Math.Min(pwd.Length, 32);
        Array.Copy(pwd, padded, len);
        Array.Copy(Pad, 0, padded, len, 32 - len);
        return padded;
    }

    /// <summary>
    /// PDF 32000 Algorithm 2 for revision 3: file encryption key from the
    /// user password. R3 always performs the 50 MD5 iterations — they are
    /// tied to the REVISION, not the key length (this also applies to
    /// 40-bit keys, using 5-byte truncation).
    /// </summary>
    private static byte[] DeriveFileKey(string userPassword, byte[] oValue, int permissions, byte[] docId, int keyLenBytes)
    {
        var pBytes = new byte[]
        {
            (byte)(permissions & 0xFF), (byte)((permissions >> 8) & 0xFF),
            (byte)((permissions >> 16) & 0xFF), (byte)((permissions >> 24) & 0xFF),
        };
        var hash = Md5(PadPassword(userPassword), oValue, pBytes, docId);
        for (int i = 0; i < 50; i++)
        {
            var trunc = new byte[keyLenBytes];
            Array.Copy(hash, trunc, keyLenBytes);
            hash = Md5(trunc);
        }
        var key = new byte[keyLenBytes];
        Array.Copy(hash, key, keyLenBytes);
        return key;
    }

    /// <summary>PDF 32000 Algorithm 5 (R3): recompute U and compare its first 16 bytes.</summary>
    private static void AssertUMatchesR3(byte[] fileKey, byte[] docId, byte[] uValue)
    {
        var expected = Rc4(fileKey, Md5(Pad, docId));
        for (int round = 1; round <= 19; round++)
        {
            var roundKey = new byte[fileKey.Length];
            for (int i = 0; i < fileKey.Length; i++) roundKey[i] = (byte)(fileKey[i] ^ round);
            expected = Rc4(roundKey, expected);
        }
        for (int i = 0; i < 16; i++)
            uValue[i].Should().Be(expected[i],
                "a spec-compliant R3 reader must be able to verify the user password");
    }

    /// <summary>PDF 32000 Algorithm 1: per-object key.</summary>
    private static byte[] ObjectKey(byte[] fileKey, int objNum)
    {
        var input = new byte[fileKey.Length + 5];
        Array.Copy(fileKey, input, fileKey.Length);
        input[fileKey.Length] = (byte)(objNum & 0xFF);
        input[fileKey.Length + 1] = (byte)((objNum >> 8) & 0xFF);
        input[fileKey.Length + 2] = (byte)((objNum >> 16) & 0xFF);
        // generation 0 -> two zero bytes already in place
        var hash = Md5(input);
        var key = new byte[Math.Min(fileKey.Length + 5, 16)];
        Array.Copy(hash, key, key.Length);
        return key;
    }

    private static string Latin1(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    private static byte[] HexToBytes(string hex)
    {
        var result = new byte[hex.Length / 2];
        for (int i = 0; i < result.Length; i++)
            result[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return result;
    }

    private static (byte[] o, byte[] u, int p, byte[] id, int lengthBits) ReadEncryptDict(string text)
    {
        var o = HexToBytes(Regex.Match(text, @"/O <([0-9A-Fa-f]+)>").Groups[1].Value);
        var u = HexToBytes(Regex.Match(text, @"/U <([0-9A-Fa-f]+)>").Groups[1].Value);
        int p = int.Parse(Regex.Match(text, @"/P (-?\d+)").Groups[1].Value);
        var id = HexToBytes(Regex.Match(text, @"/ID \[<([0-9A-Fa-f]+)>").Groups[1].Value);
        int lengthBits = int.Parse(Regex.Match(text, @"/Encrypt << [^>]*?/Length (\d+)").Groups[1].Value);
        return (o, u, p, id, lengthBits);
    }

    /// <summary>Extract a stream's bytes and its owning object number.</summary>
    private static (byte[] data, int objNum) ExtractStream(byte[] pdf, string text, int searchFrom)
    {
        int streamIdx = text.IndexOf("stream\n", searchFrom, StringComparison.Ordinal);
        streamIdx.Should().BeGreaterThan(0);
        int dataStart = streamIdx + "stream\n".Length;
        int lenIdx = text.LastIndexOf("/Length ", streamIdx, StringComparison.Ordinal);
        int numEnd = lenIdx + "/Length ".Length;
        int numStart = numEnd;
        while (numEnd < text.Length && char.IsDigit(text[numEnd])) numEnd++;
        int length = int.Parse(text.Substring(numStart, numEnd - numStart));

        int objIdx = text.LastIndexOf(" 0 obj", streamIdx, StringComparison.Ordinal);
        int objNumStart = objIdx;
        while (objNumStart > 0 && char.IsDigit(text[objNumStart - 1])) objNumStart--;
        int objNum = int.Parse(text.Substring(objNumStart, objIdx - objNumStart));

        var data = new byte[length];
        Array.Copy(pdf, dataStart, data, 0, length);
        return (data, objNum);
    }

    private static byte[] InflateZlib(byte[] data)
    {
        using var input = new MemoryStream(data, 2, data.Length - 2);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] RenderEncrypted(PdfEncryption encryption, bool compress = false, string? title = null)
    {
        var doc = new PdfDocument { Encryption = encryption, CompressContentStreams = compress, Title = title };
        var page = doc.AddPage(595, 842);
        page.AddText(Secret, 50, 700, "Helvetica", 12);
        page.AddLink(50, 690, 100, 14, "https://example.com/private-link");
        return doc.ToByteArray();
    }

    // ---------- tests ----------

    [Fact]
    public void Encrypted_ContentStreamAndStrings_AreNotPlaintext()
    {
        var pdf = RenderEncrypted(new PdfEncryption { OwnerPassword = "owner" }, title: "Hidden Title");
        var text = Latin1(pdf);

        text.Should().NotContain(Secret, "content stream bytes must be RC4-encrypted");
        text.Should().NotContain("Tj", "text-showing operators must not be readable in an encrypted PDF");
        text.Should().NotContain("private-link", "annotation URI strings must be encrypted");
        text.Should().NotContain("Hidden Title", "Info dictionary strings must be encrypted");
        text.Should().Contain("/Encrypt <<");
        text.Should().Contain("/ID [<");
    }

    [Fact]
    public void Encrypted_SpecReader_VerifiesUserPasswordViaUValue()
    {
        var pdf = RenderEncrypted(new PdfEncryption { OwnerPassword = "owner", UserPassword = "user1" });
        var (o, u, p, id, lengthBits) = ReadEncryptDict(Latin1(pdf));

        var fileKey = DeriveFileKey("user1", o, p, id, lengthBits / 8);
        AssertUMatchesR3(fileKey, id, u);
    }

    [Fact]
    public void Encrypted_SpecReader_DecryptsContentStream()
    {
        var pdf = RenderEncrypted(new PdfEncryption { OwnerPassword = "owner" });
        var text = Latin1(pdf);
        var (o, _, p, id, lengthBits) = ReadEncryptDict(text);
        var fileKey = DeriveFileKey("", o, p, id, lengthBits / 8); // empty user password

        var (cipher, objNum) = ExtractStream(pdf, text, 0);
        var plain = Encoding.Latin1.GetString(Rc4(ObjectKey(fileKey, objNum), cipher));

        plain.Should().Contain("(" + Secret + ") Tj",
            "decrypting with the spec-derived per-object key must recover the operators");
    }

    [Fact]
    public void Encrypted_WithCompression_DecryptsThenInflates()
    {
        var pdf = RenderEncrypted(new PdfEncryption { OwnerPassword = "owner" }, compress: true);
        var text = Latin1(pdf);
        var (o, _, p, id, lengthBits) = ReadEncryptDict(text);
        var fileKey = DeriveFileKey("", o, p, id, lengthBits / 8);

        var (cipher, objNum) = ExtractStream(pdf, text, 0);
        var inflated = InflateZlib(Rc4(ObjectKey(fileKey, objNum), cipher));

        Encoding.Latin1.GetString(inflated).Should().Contain("(" + Secret + ") Tj",
            "compression must be applied before encryption (decrypt, then inflate)");
    }

    [Fact]
    public void Encrypted_SpecReader_DecryptsUriString()
    {
        var pdf = RenderEncrypted(new PdfEncryption { OwnerPassword = "owner" });
        var text = Latin1(pdf);
        var (o, _, p, id, lengthBits) = ReadEncryptDict(text);
        var fileKey = DeriveFileKey("", o, p, id, lengthBits / 8);

        // The URI hex string lives in the page dictionary object
        // "/Type /Page /Parent" pins the page dict ("/Type /Pages" is the page tree)
        var uriMatch = Regex.Match(text, @"(\d+) 0 obj\s*<< /Type /Page /Parent[\s\S]*?/URI <([0-9A-Fa-f]+)>");
        uriMatch.Success.Should().BeTrue("the URI must be written as an encrypted hex string");

        int pageObjNum = int.Parse(uriMatch.Groups[1].Value);
        var plain = Encoding.Latin1.GetString(
            Rc4(ObjectKey(fileKey, pageObjNum), HexToBytes(uriMatch.Groups[2].Value)));
        plain.Should().Be("https://example.com/private-link");
    }

    [Fact]
    public void Encrypted_40Bit_UsesR3AlgorithmsAndRoundtrips()
    {
        var pdf = RenderEncrypted(new PdfEncryption { OwnerPassword = "owner", KeyLength = 40 });
        var text = Latin1(pdf);
        var (o, u, p, id, lengthBits) = ReadEncryptDict(text);
        lengthBits.Should().Be(40);
        var fileKey = DeriveFileKey("", o, p, id, lengthBits / 8);

        // The dictionary declares /R 3, so O/U/key derivation must use the
        // R3 algorithms even with a 40-bit key — otherwise compliant viewers
        // reject the correct password.
        AssertUMatchesR3(fileKey, id, u);

        var (cipher, objNum) = ExtractStream(pdf, text, 0);
        Encoding.Latin1.GetString(Rc4(ObjectKey(fileKey, objNum), cipher))
            .Should().Contain("(" + Secret + ") Tj");
    }

    [Fact]
    public void PermissionOnly_NoPasswords_StillEncrypts()
    {
        // The common view-only case: no passwords, only permission flags.
        // This must produce an encrypted PDF (empty user password), not a
        // silently unrestricted plaintext one.
        var pdf = RenderEncrypted(new PdfEncryption { AllowCopying = false, AllowPrinting = false });
        var text = Latin1(pdf);

        text.Should().Contain("/Encrypt <<", "permission flags require the encryption dictionary");
        text.Should().NotContain(Secret);

        var (o, u, p, id, lengthBits) = ReadEncryptDict(text);
        (p & 0x10).Should().Be(0, "copying must be disallowed");
        var fileKey = DeriveFileKey("", o, p, id, lengthBits / 8);
        AssertUMatchesR3(fileKey, id, u);
    }

    [Fact]
    public void InvalidKeyLength_ThrowsClearError()
    {
        var act = () => RenderEncrypted(new PdfEncryption { OwnerPassword = "x", KeyLength = 256 });
        act.Should().Throw<ArgumentException>(
            "the RC4 Standard handler supports only 40- and 128-bit keys");
    }

    [Fact]
    public void TwoDocumentsBackToBack_GetDistinctDocumentIds()
    {
        var a = RenderEncrypted(new PdfEncryption { OwnerPassword = "owner" });
        var b = RenderEncrypted(new PdfEncryption { OwnerPassword = "owner" });

        var idA = ReadEncryptDict(Latin1(a)).id;
        var idB = ReadEncryptDict(Latin1(b)).id;
        idA.Should().NotEqual(idB,
            "/ID must be unique per document even for documents created in the same clock tick");
    }

    [Fact]
    public void Permissions_MapToPValueBits()
    {
        var pdf = RenderEncrypted(new PdfEncryption
        {
            OwnerPassword = "owner",
            AllowPrinting = false,
            AllowCopying = false,
            AllowModifying = false,
        });
        var (_, _, p, _, _) = ReadEncryptDict(Latin1(pdf));

        (p & 0x04).Should().Be(0, "printing must be disallowed (bit 3)");
        (p & 0x08).Should().Be(0, "modifying must be disallowed (bit 4)");
        (p & 0x10).Should().Be(0, "copying must be disallowed (bit 5)");
    }

    [Fact]
    public void HtmlToPdf_RenderWithEncryption_ProducesDecryptablePdf()
    {
        var pdf = HtmlToPdf.Render(
            "<html><body><p>Encrypted pipeline text</p></body></html>",
            new PdfEncryption { OwnerPassword = "owner", AllowCopying = false });
        var text = Latin1(pdf);

        text.Should().Contain("/Encrypt <<");
        text.Should().NotContain("Encrypted pipeline text");

        var (o, _, p, id, lengthBits) = ReadEncryptDict(text);
        var fileKey = DeriveFileKey("", o, p, id, lengthBits / 8);
        var (cipher, objNum) = ExtractStream(pdf, text, 0);
        Encoding.Latin1.GetString(Rc4(ObjectKey(fileKey, objNum), cipher))
            .Should().Contain("Encrypted pipeline text");
    }

    [Fact]
    public void NoEncryption_OutputUnchanged()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(595, 842);
        page.AddText(Secret, 50, 700, "Helvetica", 12);
        var text = Latin1(doc.ToByteArray());

        text.Should().Contain("(" + Secret + ") Tj");
        text.Should().NotContain("/Encrypt");
    }
}
