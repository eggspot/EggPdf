using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// PDF encryption using RC4 (40/128-bit) for PDF 1.7 compatibility.
/// Supports user password, owner password, and permission flags.
/// </summary>
public class PdfEncryption
{
    /// <summary>User password (opens the document). Empty = no password required to open.</summary>
    public string UserPassword { get; set; } = "";

    /// <summary>Owner password (full access). Required for encryption.</summary>
    public string OwnerPassword { get; set; } = "";

    /// <summary>Allow printing.</summary>
    public bool AllowPrinting { get; set; } = true;

    /// <summary>Allow copying text.</summary>
    public bool AllowCopying { get; set; } = true;

    /// <summary>Allow modifying the document.</summary>
    public bool AllowModifying { get; set; } = false;

    /// <summary>Key length in bits (40 or 128).</summary>
    public int KeyLength { get; set; } = 128;

    // PDF password padding string (from spec)
    private static readonly byte[] PasswordPadding = new byte[]
    {
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41,
        0x64, 0x00, 0x4B, 0x49, 0x43, 0x28, 0x46, 0x57,
        0x44, 0x28, 0x55, 0x78, 0x65, 0x63, 0x68, 0x6E,
        0x69, 0x63, 0x61, 0x6C, 0x20, 0x49, 0x6E, 0x66
    };

    /// <summary>Compute encryption parameters for the PDF trailer.</summary>
    public EncryptionParams Compute(byte[] documentId)
    {
        if (KeyLength != 40 && KeyLength != 128)
            throw new ArgumentException(
                $"KeyLength must be 40 or 128 for the RC4 Standard security handler (got {KeyLength}).");

        int keyLen = KeyLength / 8; // bytes
        int permissions = ComputePermissions();

        // Pad passwords
        byte[] userPwd = PadPassword(Encoding.GetEncoding(28591).GetBytes(UserPassword));
        byte[] ownerPwd = PadPassword(Encoding.GetEncoding(28591).GetBytes(
            string.IsNullOrEmpty(OwnerPassword) ? UserPassword : OwnerPassword));

        // Compute O value (owner password hash)
        byte[] oValue = ComputeOValue(ownerPwd, userPwd, keyLen);

        // Compute encryption key
        byte[] encKey = ComputeEncryptionKey(userPwd, oValue, permissions, documentId, keyLen);

        // Compute U value (user password verification)
        byte[] uValue = ComputeUValue(encKey, documentId, keyLen);

        return new EncryptionParams
        {
            OValue = oValue,
            UValue = uValue,
            Permissions = permissions,
            KeyLength = KeyLength,
            EncryptionKey = encKey,
        };
    }

    private int ComputePermissions()
    {
        int p = unchecked((int)0xFFFFF000); // bits 13-32 set
        p |= 0x00000C00; // bits 10, 11 reserved (set)
        if (AllowPrinting) p |= 0x00000004;  // bit 3
        if (AllowModifying) p |= 0x00000008; // bit 4
        if (AllowCopying) p |= 0x00000010;   // bit 5
        p |= 0x00000020; // bit 6: annotations (always allow)
        return p;
    }

    private static byte[] PadPassword(byte[] pwd)
    {
        var padded = new byte[32];
        int len = Math.Min(pwd.Length, 32);
        Array.Copy(pwd, 0, padded, 0, len);
        if (len < 32)
            Array.Copy(PasswordPadding, 0, padded, len, 32 - len);
        return padded;
    }

    private static byte[] ComputeOValue(byte[] ownerPwd, byte[] userPwd, int keyLen)
    {
        using var md5 = MD5.Create();
        byte[] hash = md5.ComputeHash(ownerPwd);

        // Revision 3 (which the writer always declares): 50 MD5 iterations
        // and 19 RC4 rounds regardless of key length — they are tied to the
        // REVISION, not to 40 vs 128 bits (ISO 32000-1 Algorithm 3).
        for (int i = 0; i < 50; i++)
            hash = md5.ComputeHash(hash);

        byte[] key = new byte[keyLen];
        Array.Copy(hash, 0, key, 0, keyLen);

        byte[] result = RC4(key, userPwd);

        for (int round = 1; round <= 19; round++)
        {
            byte[] roundKey = new byte[keyLen];
            for (int j = 0; j < keyLen; j++)
                roundKey[j] = (byte)(key[j] ^ round);
            result = RC4(roundKey, result);
        }

        return result;
    }

    private static byte[] ComputeEncryptionKey(byte[] userPwd, byte[] oValue, int permissions, byte[] documentId, int keyLen)
    {
        using var md5 = MD5.Create();
        using var ms = new MemoryStream();
        ms.Write(userPwd, 0, userPwd.Length);
        ms.Write(oValue, 0, oValue.Length);

        ms.WriteByte((byte)(permissions & 0xFF));
        ms.WriteByte((byte)((permissions >> 8) & 0xFF));
        ms.WriteByte((byte)((permissions >> 16) & 0xFF));
        ms.WriteByte((byte)((permissions >> 24) & 0xFF));

        ms.Write(documentId, 0, documentId.Length);

        byte[] hash = md5.ComputeHash(ms.ToArray());

        // R3 Algorithm 2: 50 iterations over the TRUNCATED hash, for any key length
        for (int i = 0; i < 50; i++)
        {
            var trunc = new byte[keyLen];
            Array.Copy(hash, 0, trunc, 0, keyLen);
            hash = md5.ComputeHash(trunc);
        }

        byte[] key = new byte[keyLen];
        Array.Copy(hash, 0, key, 0, keyLen);
        return key;
    }

    private static byte[] ComputeUValue(byte[] encKey, byte[] documentId, int keyLen)
    {
        // R3 Algorithm 5 for every key length: MD5(padding + documentId),
        // RC4, then 19 XOR rounds, padded to 32 bytes.
        using var md5 = MD5.Create();
        using var ms = new MemoryStream();
        ms.Write(PasswordPadding, 0, PasswordPadding.Length);
        ms.Write(documentId, 0, documentId.Length);
        byte[] hash = md5.ComputeHash(ms.ToArray());

        byte[] result = RC4(encKey, hash);
        for (int round = 1; round <= 19; round++)
        {
            byte[] roundKey = new byte[keyLen];
            for (int j = 0; j < keyLen; j++)
                roundKey[j] = (byte)(encKey[j] ^ round);
            result = RC4(roundKey, result);
        }

        // Pad to 32 bytes
        byte[] uValue = new byte[32];
        Array.Copy(result, 0, uValue, 0, Math.Min(result.Length, 16));
        return uValue;
    }

    /// <summary>
    /// Encrypt a stream's or string's bytes for the object that contains it
    /// (PDF 32000 Algorithm 1): RC4 with MD5(fileKey + objNum[3 LE] + gen[2 LE])
    /// truncated to min(keyLen + 5, 16) bytes. RC4 preserves length, so
    /// /Length values written for the ciphertext match the plaintext.
    /// </summary>
    internal static byte[] EncryptForObject(byte[] fileKey, int objNum, int generation, byte[] data)
    {
        var input = new byte[fileKey.Length + 5];
        Array.Copy(fileKey, input, fileKey.Length);
        input[fileKey.Length] = (byte)(objNum & 0xFF);
        input[fileKey.Length + 1] = (byte)((objNum >> 8) & 0xFF);
        input[fileKey.Length + 2] = (byte)((objNum >> 16) & 0xFF);
        input[fileKey.Length + 3] = (byte)(generation & 0xFF);
        input[fileKey.Length + 4] = (byte)((generation >> 8) & 0xFF);

        byte[] hash;
        using (var md5 = MD5.Create())
            hash = md5.ComputeHash(input);

        var objKey = new byte[Math.Min(fileKey.Length + 5, 16)];
        Array.Copy(hash, objKey, objKey.Length);
        return RC4(objKey, data);
    }

    /// <summary>RC4 encryption/decryption.</summary>
    internal static byte[] RC4(byte[] key, byte[] data)
    {
        // RC4 key schedule
        byte[] s = new byte[256];
        for (int i = 0; i < 256; i++) s[i] = (byte)i;

        int j = 0;
        for (int i = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            byte tmp = s[i]; s[i] = s[j]; s[j] = tmp;
        }

        // RC4 PRGA
        var result = new byte[data.Length];
        int x = 0, y = 0;
        for (int i = 0; i < data.Length; i++)
        {
            x = (x + 1) & 0xFF;
            y = (y + s[x]) & 0xFF;
            byte tmp = s[x]; s[x] = s[y]; s[y] = tmp;
            result[i] = (byte)(data[i] ^ s[(s[x] + s[y]) & 0xFF]);
        }
        return result;
    }
}

/// <summary>Computed encryption parameters for PDF output.</summary>
public class EncryptionParams
{
    public byte[] OValue { get; set; } = Array.Empty<byte>();
    public byte[] UValue { get; set; } = Array.Empty<byte>();
    public int Permissions { get; set; }
    public int KeyLength { get; set; }
    public byte[] EncryptionKey { get; set; } = Array.Empty<byte>();
}
