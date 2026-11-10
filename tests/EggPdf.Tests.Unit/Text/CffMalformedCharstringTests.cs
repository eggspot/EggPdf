using System;
using System.Collections.Generic;
using System.Linq;
using EggPdf.Text.TrueType;
using FluentAssertions;
using Xunit;

namespace EggPdf.Tests.Unit.Text;

public class CffMalformedCharstringTests
{
    private const byte CallSubr = 10, Endchar = 14, Vsindex = 15, Blend = 16, Escape = 12, Dup = 27, Rmoveto = 21, CallGsubr = 29;
    private const byte TopDictCharStringsOperator = 17, LongIntPrefix = 29;

    private static byte[] Num(int v) => v >= -107 && v <= 107 ? new[] { (byte)(v + 139) } : new byte[] { 28, (byte)(v >> 8), (byte)v };

    private static byte[] Index(params byte[][] items)
    {
        var bytes = new List<byte> { (byte)(items.Length >> 8), (byte)items.Length };
        if (items.Length == 0) return bytes.ToArray();
        bytes.Add(4);
        int offset = 1;
        void Offset(int v) { bytes.Add((byte)(v >> 24)); bytes.Add((byte)(v >> 16)); bytes.Add((byte)(v >> 8)); bytes.Add((byte)v); }
        Offset(offset);
        foreach (var item in items) { offset += item.Length; Offset(offset); }
        foreach (var item in items) bytes.AddRange(item);
        return bytes.ToArray();
    }

    private static CffFont Font(byte[] charString)
    {
        var header = new byte[] { 1, 0, 4, 1 };
        var name = Index(new[] { (byte)'T' });
        var strings = Index();
        var globalSubrs = Index();
        byte[] TopDict(int charStringsOffset) => Index(new[] { LongIntPrefix }
            .Concat(new[] { (byte)(charStringsOffset >> 24), (byte)(charStringsOffset >> 16), (byte)(charStringsOffset >> 8), (byte)charStringsOffset })
            .Concat(new[] { TopDictCharStringsOperator }).ToArray());
        int charStringsAt = header.Length + name.Length + TopDict(0).Length + strings.Length + globalSubrs.Length;
        var cff = header.Concat(name).Concat(TopDict(charStringsAt)).Concat(strings).Concat(globalSubrs)
            .Concat(Index(new[] { Endchar }, charString)).ToArray();
        var font = CffFont.TryParse(cff, 0, false, null);
        font.Should().NotBeNull();
        return font!;
    }

    [Theory]
    [InlineData(CallSubr)]
    [InlineData(CallGsubr)]
    [InlineData(Vsindex)]
    [InlineData(Blend)]
    public void GetContours_OperatorPoppingAnEmptyStack_DoesNotThrow(byte op)
    {
        var font = Font(new[] { op, Endchar });

        var act = () => font.GetContours(1, 1f);

        act.Should().NotThrow("a malformed charstring must degrade to fewer contours, never an exception");
    }

    [Fact]
    public void GetContours_DupOnAFullStack_DoesNotThrow()
    {
        var charString = Enumerable.Repeat(Num(1), 520).SelectMany(b => b)
            .Concat(new[] { Escape, Dup, Escape, Dup, (byte)0x8B, (byte)0x8B, Rmoveto, Endchar }).ToArray();
        var font = Font(charString);

        var act = () => font.GetContours(1, 1f);

        act.Should().NotThrow();
    }
}
