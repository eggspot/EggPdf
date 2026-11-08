namespace EggPdf.Core;

/// <summary>
/// A 64-bit FNV-1a hash that is the same in every process, run and platform (unlike
/// <see cref="string.GetHashCode()"/>), for naming PDF resources so identical input produces identical bytes.
/// </summary>
public static class StableHash
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    /// <summary>The hash of the string's UTF-16 code units as 16 upper-case hex digits.</summary>
    public static string Hex(string value)
    {
        ulong hash = OffsetBasis;
        unchecked
        {
            foreach (char c in value)
            {
                hash = (hash ^ (byte)c) * Prime;
                hash = (hash ^ (byte)(c >> 8)) * Prime;
            }
        }
        return ToHex(hash);
    }

    /// <summary>The hash of the bytes as 16 upper-case hex digits.</summary>
    public static string Hex(byte[] data)
    {
        ulong hash = OffsetBasis;
        unchecked
        {
            for (int i = 0; i < data.Length; i++)
                hash = (hash ^ data[i]) * Prime;
        }
        return ToHex(hash);
    }

    private static string ToHex(ulong hash)
    {
        const string digits = "0123456789ABCDEF";
        var chars = new char[16];
        for (int i = 15; i >= 0; i--)
        {
            chars[i] = digits[(int)(hash & 0xF)];
            hash >>= 4;
        }
        return new string(chars);
    }
}
