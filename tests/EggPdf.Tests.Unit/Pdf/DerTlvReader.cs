using System;

namespace EggPdf.Tests.Unit.Pdf;

/// <summary>
/// Reads a DER TLV's total encoded length (tag + length header + content)
/// from its first bytes. Used to recover a CMS/PKCS#7 blob embedded in a
/// zero-padded fixed-width field: the padding can't be found by trimming
/// trailing zero bytes, because a genuine signature can itself end in 0x00.
/// </summary>
internal static class DerTlvReader
{
    public static int ReadTotalLength(byte[] data)
    {
        int lenByte = data[1];
        if (lenByte < 0x80)
            return 2 + lenByte;

        int numLenBytes = lenByte & 0x7F;
        int contentLen = 0;
        for (int i = 0; i < numLenBytes; i++)
            contentLen = (contentLen << 8) | data[2 + i];
        return 2 + numLenBytes + contentLen;
    }
}
