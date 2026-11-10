using EggPdf.Text.OpenType;

namespace EggPdf.Text.TrueType;

internal sealed class ClassPairKerning
{
    private const int ClassPairFormat = 2;
    private const int XPlacementFlag = 0x0001;
    private const int YPlacementFlag = 0x0002;
    private const int XAdvanceFlag = 0x0004;
    private const int RecordsOffset = 16;

    private readonly OtReader _reader;
    private readonly Coverage _coverage;
    private readonly ClassDef? _firstClasses;
    private readonly ClassDef? _secondClasses;
    private readonly int _firstClassCount;
    private readonly int _secondClassCount;
    private readonly int _records;
    private readonly int _recordSize;
    private readonly int _xAdvanceOffset;

    private ClassPairKerning(OtReader reader, Coverage coverage, ClassDef? firstClasses, ClassDef? secondClasses,
        int firstClassCount, int secondClassCount, int records, int recordSize, int xAdvanceOffset)
    {
        _reader = reader;
        _coverage = coverage;
        _firstClasses = firstClasses;
        _secondClasses = secondClasses;
        _firstClassCount = firstClassCount;
        _secondClassCount = secondClassCount;
        _records = records;
        _recordSize = recordSize;
        _xAdvanceOffset = xAdvanceOffset;
    }

    public static ClassPairKerning? Parse(byte[] data, int subtableOffset)
    {
        var reader = new OtReader(data);
        if (reader.U16(subtableOffset) != ClassPairFormat) return null;
        int firstValueFormat = reader.U16(subtableOffset + 4);
        if ((firstValueFormat & XAdvanceFlag) == 0) return null;
        var coverage = Coverage.Parse(reader, subtableOffset + reader.U16(subtableOffset + 2));
        if (coverage == null) return null;

        int secondValueFormat = reader.U16(subtableOffset + 6);
        int xAdvanceOffset = ((firstValueFormat & XPlacementFlag) != 0 ? 2 : 0) +
                             ((firstValueFormat & YPlacementFlag) != 0 ? 2 : 0);
        return new ClassPairKerning(reader, coverage,
            ClassDef.Parse(reader, subtableOffset + reader.U16(subtableOffset + 8)),
            ClassDef.Parse(reader, subtableOffset + reader.U16(subtableOffset + 10)),
            reader.U16(subtableOffset + 12), reader.U16(subtableOffset + 14),
            subtableOffset + RecordsOffset,
            ValueRecord.SizeOf(firstValueFormat) + ValueRecord.SizeOf(secondValueFormat),
            xAdvanceOffset);
    }

    public bool TryGetKerning(ushort leftGlyph, ushort rightGlyph, out int xAdvance)
    {
        xAdvance = 0;
        if (_coverage.IndexOf(leftGlyph) < 0) return false;
        int firstClass = _firstClasses?.GetClass(leftGlyph) ?? 0;
        int secondClass = _secondClasses?.GetClass(rightGlyph) ?? 0;
        if (firstClass >= _firstClassCount || secondClass >= _secondClassCount) return false;
        int record = _records + (firstClass * _secondClassCount + secondClass) * _recordSize;
        xAdvance = _reader.I16(record + _xAdvanceOffset);
        return true;
    }
}
