using System;
using System.Collections.Generic;

namespace EggPdf.Pdf;

/// <summary>
/// Assigns sequential PDF object numbers and tracks their byte offsets for
/// the cross-reference table. Centralizing allocation here means a forgotten
/// increment can no longer silently misnumber a later object — every ID in
/// use came from <see cref="Allocate"/>, and recording an offset for an
/// unallocated or already-recorded object throws immediately instead of
/// producing a wrong xref entry.
/// </summary>
internal sealed class PdfObjectAllocator
{
    private readonly Dictionary<int, long> _offsets = new();
    private int _nextObj = 1;

    /// <summary>Highest object number allocated so far (0 when none yet).</summary>
    public int Count => _nextObj - 1;

    /// <summary>Reserve and return the next sequential object number.</summary>
    public int Allocate() => _nextObj++;

    /// <summary>Record the byte offset an allocated object was written at.</summary>
    public void RecordOffset(int objNum, long offset)
    {
        if (objNum < 1 || objNum > Count)
            throw new InvalidOperationException($"Object {objNum} was never allocated.");
        if (_offsets.ContainsKey(objNum))
            throw new InvalidOperationException($"Object {objNum} already has a recorded offset.");
        _offsets[objNum] = offset;
    }

    /// <summary>Look up a recorded offset; false when the object was allocated but never written.</summary>
    public bool TryGetOffset(int objNum, out long offset) => _offsets.TryGetValue(objNum, out offset);
}
