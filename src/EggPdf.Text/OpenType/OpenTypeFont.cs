using System;
using System.Collections.Generic;

namespace EggPdf.Text.OpenType;

/// <summary>
/// The parsed GSUB/GPOS/GDEF layout data of one font, built lazily on the first complex-script
/// shape so fonts that only ever render Latin never pay for it.
/// </summary>
internal sealed class OpenTypeFont
{
    public readonly OtEngine? Gsub;
    public readonly OtEngine? Gpos;
    public readonly Gdef? Gdef;

    private OpenTypeFont(OtEngine? gsub, OtEngine? gpos, Gdef? gdef)
    {
        Gsub = gsub; Gpos = gpos; Gdef = gdef;
    }

    public static OpenTypeFont Load(byte[] data)
    {
        var tables = SfntDirectory.Read(data);
        Gdef? gdef = null;
        if (tables.TryGetValue("GDEF", out var gd))
            gdef = Gdef.Parse(data, (int)gd.offset);

        OtEngine? gsub = null, gpos = null;
        if (tables.TryGetValue("GSUB", out var gs))
        {
            var layout = OtLayout.Parse(data, (int)gs.offset, isGpos: false);
            if (layout != null) gsub = new OtEngine(layout, gdef);
        }
        if (tables.TryGetValue("GPOS", out var gp))
        {
            var layout = OtLayout.Parse(data, (int)gp.offset, isGpos: true);
            if (layout != null) gpos = new OtEngine(layout, gdef);
        }
        return new OpenTypeFont(gsub, gpos, gdef);
    }
}

/// <summary>Reads an sfnt table directory, including the first face of a TrueType Collection.</summary>
internal static class SfntDirectory
{
    public static Dictionary<string, (uint offset, uint length)> Read(byte[] data)
    {
        var tables = new Dictionary<string, (uint offset, uint length)>();
        var r = new OtReader(data);
        int pos = 0;
        if (r.Tag(0) == "ttcf")
            pos = (int)r.U32(12); // offset of the first face's table directory

        int numTables = r.U16(pos + 4);
        int rec = pos + 12;
        for (int i = 0; i < numTables; i++, rec += 16)
            tables[r.Tag(rec)] = (r.U32(rec + 8), r.U32(rec + 12));
        return tables;
    }
}
