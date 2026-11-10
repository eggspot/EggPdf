using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using EggPdf.Pdf;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Pdf;

public class PdfEncryptionPasswordTests
{
    private const int PrintBit = 1 << 2;
    private const int ReservedBits7And8 = (1 << 6) | (1 << 7);
    private const int AccessibilityBit = 1 << 9;
    private const int HighQualityPrintBit = 1 << 11;

    private static readonly byte[] Padding =
    {
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4B, 0x49, 0x43, 0x28, 0x46, 0x57,
        0x44, 0x28, 0x55, 0x78, 0x65, 0x63, 0x68, 0x6E, 0x69, 0x63, 0x61, 0x6C, 0x20, 0x49, 0x6E, 0x66,
    };

    private static readonly byte[] DocumentId = Enumerable.Range(1, 16).Select(i => (byte)(i * 7)).ToArray();

    [Theory]
    [InlineData(40)]
    [InlineData(128)]
    public void OwnerPassword_AuthenticatesLikeAReader(int keyLength)
    {
        var encryption = new PdfEncryption { UserPassword = "user", OwnerPassword = "owner", KeyLength = keyLength };

        var computed = encryption.Compute(DocumentId);

        var recoveredUserPassword = RecoverUserPasswordFromOwner("owner", computed.OValue, keyLength / 8);
        recoveredUserPassword.Should().Equal(Pad("user"), "Algorithm 7 decrypts O with the owner password back to the padded user password");
        UserPasswordMatches(recoveredUserPassword, computed, keyLength / 8).Should().BeTrue();
    }

    [Theory]
    [InlineData(40)]
    [InlineData(128)]
    public void UserPassword_AuthenticatesLikeAReader(int keyLength)
    {
        var encryption = new PdfEncryption { UserPassword = "user", OwnerPassword = "owner", KeyLength = keyLength };

        var computed = encryption.Compute(DocumentId);

        UserPasswordMatches(Pad("user"), computed, keyLength / 8).Should().BeTrue();
        UserPasswordMatches(Pad("wrong"), computed, keyLength / 8).Should().BeFalse();
    }

    [Fact]
    public void Permissions_SetTheReservedAndAccessibilityBits()
    {
        var p = new PdfEncryption { OwnerPassword = "o" }.Compute(DocumentId).Permissions;

        (p & ReservedBits7And8).Should().Be(ReservedBits7And8, "ISO 32000 requires reserved bits 7 and 8 to be 1");
        (p & AccessibilityBit).Should().Be(AccessibilityBit, "text stays extractable for screen readers");
    }

    [Fact]
    public void Permissions_PrintingDisallowed_ClearsHighQualityPrintToo()
    {
        var p = new PdfEncryption { OwnerPassword = "o", AllowPrinting = false }.Compute(DocumentId).Permissions;

        (p & PrintBit).Should().Be(0);
        (p & HighQualityPrintBit).Should().Be(0);
    }

    private static byte[] Pad(string password)
    {
        var bytes = Encoding.Latin1.GetBytes(password);
        return bytes.Take(32).Concat(Padding).Take(32).ToArray();
    }

    private static byte[] RecoverUserPasswordFromOwner(string ownerPassword, byte[] oValue, int keyBytes)
    {
        var hash = MD5.HashData(Pad(ownerPassword));
        for (int i = 0; i < 50; i++)
            hash = MD5.HashData(hash.Take(keyBytes).ToArray());
        var key = hash.Take(keyBytes).ToArray();

        var result = oValue.ToArray();
        for (int round = 19; round >= 0; round--)
            result = Rc4(key.Select(b => (byte)(b ^ round)).ToArray(), result);
        return result;
    }

    private static bool UserPasswordMatches(byte[] paddedUserPassword, EncryptionParams computed, int keyBytes)
    {
        var input = paddedUserPassword
            .Concat(computed.OValue)
            .Concat(BitConverter.GetBytes(computed.Permissions))
            .Concat(DocumentId)
            .ToArray();
        var hash = MD5.HashData(input);
        for (int i = 0; i < 50; i++)
            hash = MD5.HashData(hash.Take(keyBytes).ToArray());
        var key = hash.Take(keyBytes).ToArray();

        var u = MD5.HashData(Padding.Concat(DocumentId).ToArray());
        for (int round = 0; round <= 19; round++)
            u = Rc4(key.Select(b => (byte)(b ^ round)).ToArray(), u);
        return u.SequenceEqual(computed.UValue.Take(16));
    }

    private static byte[] Rc4(byte[] key, byte[] data)
    {
        var s = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }
        var output = new byte[data.Length];
        for (int k = 0, x = 0, y = 0; k < data.Length; k++)
        {
            x = (x + 1) & 0xFF;
            y = (y + s[x]) & 0xFF;
            (s[x], s[y]) = (s[y], s[x]);
            output[k] = (byte)(data[k] ^ s[(s[x] + s[y]) & 0xFF]);
        }
        return output;
    }
}
