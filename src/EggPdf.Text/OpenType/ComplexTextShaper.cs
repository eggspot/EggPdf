using System;
using System.Collections.Generic;
using EggPdf.Text.TrueType;

namespace EggPdf.Text.OpenType;

/// <summary>A shaped glyph in visual (left-to-right drawing) order, in font units.</summary>
public readonly struct PositionedGlyph
{
    public readonly ushort GlyphId;
    /// <summary>Advance after GPOS adjustments (mark glyphs are zero-width).</summary>
    public readonly int XAdvance;
    /// <summary>Offset of the glyph's origin from its pen position.</summary>
    public readonly int XOffset, YOffset;

    public PositionedGlyph(ushort glyphId, int xAdvance, int xOffset, int yOffset)
    {
        GlyphId = glyphId; XAdvance = xAdvance; XOffset = xOffset; YOffset = yOffset;
    }
}

/// <summary>
/// Glyph-level text shaping through the font's own GSUB/GPOS tables: Thai/Lao mark handling,
/// Indic syllable reordering with conjunct/half-form substitution, Arabic mark positioning, and
/// generic ligature/kerning for whatever else shares the string. Input is logical-order text;
/// output is in visual order with mark attachments resolved into per-glyph offsets.
/// </summary>
public static partial class ComplexTextShaper
{
    private const uint MaskGlobal = 1, MaskReph = 2, MaskRephFormed = 4,
        MaskIsol = 8, MaskFina = 16, MaskMedi = 32, MaskInit = 64;

    public static bool NeedsShaping(string? text) => ComplexScripts.NeedsShaping(text);

    /// <summary>
    /// Suffix that gives complex-script text its own embedded-font key ("-CXT" Thai/Lao, "-CXA"
    /// Arabic marks, "-CX0900" Devanagari, ...), so Latin text keeps its requested typeface while
    /// complex-script boxes use a script-capable font. Null when the text needs no shaping.
    /// The first complex-script character decides (mixed-script boxes are rare).
    /// </summary>
    public static string? FontKeySuffix(string? text)
    {
        if (text == null) return null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c < 'ؐ') continue;
            if (c >= 'ऀ' && c <= '෿') return "-CX" + ComplexScripts.GetIndic(c)!.Base.ToString("X4");
            if (c >= '฀' && c <= '໿') return "-CXT";
            if (c >= 'ༀ' && c <= '࿿') return "-CXTB";
            if (c >= 'က' && c <= '႟') return "-CXM";
            if (c >= 'ក' && c <= '៿') return "-CXK";
            if (ComplexScripts.IsArabicLetterBlock(c)) return "-CXA";
        }
        return null;
    }

    /// <summary>A base letter of the script named by a <see cref="FontKeySuffix"/>, to test font coverage with.</summary>
    public static int SampleCodepoint(string suffix)
    {
        if (suffix == "-CXT") return 0x0E01;
        if (suffix == "-CXA") return 0x0628;
        if (suffix == "-CXTB") return 0x0F40;
        if (suffix == "-CXM") return 0x1000;
        if (suffix == "-CXK") return 0x1780;
        if (suffix.Length > 3 && int.TryParse(suffix.Substring(3), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out int block))
            return block + 0x15;
        return 0x0041;
    }

    /// <summary>System font families likely to cover a script, tried after the author's font-family list.</summary>
    public static string[] FallbackFontFamilies(string suffix)
    {
        switch (suffix)
        {
            case "-CXT":
                return new[] { "LeelawUI", "Leelawadee", "Tahoma", "Noto Sans Thai", "NotoSansThai", "Noto Serif Thai",
                    "Thonburi", "Garuda", "Loma", "Norasi", "Waree", "DejaVuSans" };
            case "-CXA":
                return new[] { "Arial", "Tahoma", "Segoe UI", "Noto Naskh Arabic", "NotoNaskhArabic", "DejaVuSans" };
            case "-CX0900":
                return new[] { "Nirmala", "Mangal", "Noto Sans Devanagari", "NotoSansDevanagari", "Kohinoor Devanagari",
                    "Lohit-Devanagari", "Lohit Devanagari", "Utsaah", "Aparajita", "Sanskrit Text" };
            case "-CX0980":
                return new[] { "Nirmala", "Vrinda", "Noto Sans Bengali", "NotoSansBengali", "Lohit-Bengali" };
            case "-CX0A00":
                return new[] { "Nirmala", "Raavi", "Noto Sans Gurmukhi", "NotoSansGurmukhi", "Lohit-Gurmukhi" };
            case "-CX0A80":
                return new[] { "Nirmala", "Shruti", "Noto Sans Gujarati", "NotoSansGujarati", "Lohit-Gujarati" };
            case "-CX0B00":
                return new[] { "Nirmala", "Kalinga", "Noto Sans Oriya", "NotoSansOriya", "Lohit-Oriya" };
            case "-CX0B80":
                return new[] { "Nirmala", "Latha", "Noto Sans Tamil", "NotoSansTamil", "Lohit-Tamil" };
            case "-CX0C00":
                return new[] { "Nirmala", "Gautami", "Noto Sans Telugu", "NotoSansTelugu", "Lohit-Telugu" };
            case "-CX0C80":
                return new[] { "Nirmala", "Tunga", "Noto Sans Kannada", "NotoSansKannada", "Lohit-Kannada" };
            case "-CXTB":
                return new[] { "himalaya", "Microsoft Himalaya", "Noto Serif Tibetan", "NotoSerifTibetan", "Noto Sans Tibetan",
                    "NotoSansTibetan", "Jomolhari", "Kailasa" };
            case "-CXM":
                return new[] { "mmrtext", "Myanmar Text", "Noto Sans Myanmar", "NotoSansMyanmar", "Padauk", "Myanmar MN" };
            case "-CXK":
                return new[] { "LeelawUI", "Leelawadee", "Noto Sans Khmer", "NotoSansKhmer", "Khmer UI", "Khmer OS", "DaunPenh" };
            case "-CX0D80":
                return new[] { "Nirmala", "Iskoola Pota", "iskpota", "Noto Sans Sinhala", "NotoSansSinhala" };
            case "-CX0D00":
                return new[] { "Nirmala", "Kartika", "Noto Sans Malayalam", "NotoSansMalayalam", "Lohit-Malayalam" };
            default:
                return Array.Empty<string>();
        }
    }

    public static PositionedGlyph[] Shape(FontData font, string text, bool baseRtl)
    {
        var buf = ShapeToBuffer(font, text, baseRtl);
        var result = new PositionedGlyph[buf.Count];
        for (int i = 0; i < result.Length; i++)
        {
            var g = buf.Glyphs[i];
            result[i] = new PositionedGlyph(g.Id, g.XAdvance, g.XOffset, g.YOffset);
        }
        return result;
    }

    /// <summary>Total advance of the shaped text, in font units.</summary>
    public static int MeasureUnits(FontData font, string text, bool baseRtl)
    {
        var buf = ShapeToBuffer(font, text, baseRtl);
        int sum = 0;
        for (int i = 0; i < buf.Count; i++) sum += buf.Glyphs[i].XAdvance;
        return sum;
    }

    // ── driver ──────────────────────────────────────────────────────────────────

    private static GlyphBuffer ShapeToBuffer(FontData font, string text, bool baseRtl)
    {
        var cps = new List<int>(text.Length);
        var clusters = new List<int>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                clusters.Add(i);
                cps.Add(cp);
                i++;
                continue;
            }
            clusters.Add(i);
            cps.Add(cp);
        }

        // Split into script runs; Common characters (spaces, digits, punctuation) join the run before them.
        var kinds = new ScriptKind[cps.Count];
        var indic = new IndicScriptInfo?[cps.Count];
        ScriptKind current = ScriptKind.Common;
        IndicScriptInfo? currentIndic = null;
        for (int i = 0; i < cps.Count; i++)
        {
            var k = ComplexScripts.KindOf(cps[i]);
            if (k == ScriptKind.Common) { kinds[i] = current; indic[i] = currentIndic; continue; }
            current = k;
            currentIndic = k == ScriptKind.Indic ? ComplexScripts.GetIndic(cps[i]) : null;
            kinds[i] = k; indic[i] = currentIndic;
        }
        // Leading Common characters take the first real script's kind.
        int firstReal = 0;
        while (firstReal < cps.Count && ComplexScripts.KindOf(cps[firstReal]) == ScriptKind.Common) firstReal++;
        for (int i = 0; i < firstReal && firstReal < cps.Count; i++) { kinds[i] = kinds[firstReal]; indic[i] = indic[firstReal]; }
        if (firstReal == cps.Count)
            for (int i = 0; i < cps.Count; i++) kinds[i] = ScriptKind.Latin;

        var all = new GlyphBuffer();
        int a = 0;
        while (a < cps.Count)
        {
            int b = a + 1;
            while (b < cps.Count && kinds[b] == kinds[a] && ReferenceEquals(indic[b], indic[a])) b++;
            var run = ShapeRun(font, kinds[a], indic[a], cps.GetRange(a, b - a), clusters.GetRange(a, b - a));
            int offset = all.Count;
            for (int i = 0; i < run.Count; i++)
            {
                var g = run.Glyphs[i];
                if (g.AttachTo >= 0) g.AttachTo += offset;
                if (g.CursiveParentPlusOne > 0) g.CursiveParentPlusOne += offset;
                all.Glyphs.Add(g);
            }
            a = b;
        }

        var ot = font.OpenTypeLayout;
        OrderVisually(all, text, baseRtl);

        for (int i = 0; i < all.Count; i++)
        {
            var g = all.Glyphs[i];
            if (LookupFilter.ClassOf(ot.Gdef, g) == Gdef.ClassMark) { g.XAdvance = 0; all.Glyphs[i] = g; }
        }
        ResolveAttachments(all);
        return all;
    }

    private static bool IsRtlChar(int cp)
        => (cp >= 0x0590 && cp <= 0x08FF) || (cp >= 0xFB1D && cp <= 0xFDFF) || (cp >= 0xFE70 && cp <= 0xFEFF);

    private static void OrderVisually(GlyphBuffer buf, string text, bool baseRtl)
    {
        if (buf.Count < 2 || !BidiAlgorithm.ContainsRTL(text)) return;

        var (_, logicalOrder) = BidiAlgorithm.Reorder(text, baseRtl);
        var visualPos = new int[text.Length];
        for (int v = 0; v < logicalOrder.Length && v < visualPos.Length; v++)
            if (logicalOrder[v] >= 0 && logicalOrder[v] < visualPos.Length) visualPos[logicalOrder[v]] = v;

        int n = buf.Count;
        var keys = new long[n];
        var idx = new int[n];
        for (int i = 0; i < n; i++)
        {
            var g = buf.Glyphs[i];
            int cluster = Math.Min(Math.Max(g.Cluster, 0), visualPos.Length - 1);
            long tie = IsRtlChar(g.Codepoint) ? 0xFFFFF - i : i;
            keys[i] = ((long)visualPos[cluster] << 20) | (tie & 0xFFFFF);
            idx[i] = i;
        }
        Array.Sort(keys, idx);

        var newIndexOf = new int[n];
        for (int newPos = 0; newPos < n; newPos++) newIndexOf[idx[newPos]] = newPos;
        var ordered = new List<ShapedGlyph>(n);
        for (int newPos = 0; newPos < n; newPos++)
        {
            var g = buf.Glyphs[idx[newPos]];
            if (g.AttachTo >= 0) g.AttachTo = newIndexOf[g.AttachTo];
            if (g.CursiveParentPlusOne > 0) g.CursiveParentPlusOne = newIndexOf[g.CursiveParentPlusOne - 1] + 1;
            ordered.Add(g);
        }
        buf.Glyphs.Clear();
        buf.Glyphs.AddRange(ordered);
    }

    /// <summary>Turn recorded anchor attachments into final offsets, in visual order.</summary>
    private static void ResolveAttachments(GlyphBuffer buf)
    {
        int n = buf.Count;
        var state = new byte[n]; // 0 = pending, 1 = resolving, 2 = done
        for (int i = 0; i < n; i++) Resolve(buf, i, state);
    }

    private static void Resolve(GlyphBuffer buf, int i, byte[] state)
    {
        if (state[i] != 0) return;
        state[i] = 1;
        var g = buf.Glyphs[i];
        int cp = g.CursiveParentPlusOne - 1;
        if (cp >= 0 && cp < buf.Count && cp != i && state[cp] != 1)
        {
            Resolve(buf, cp, state);
            g.YOffset += buf.Glyphs[cp].YOffset + g.CursiveDy;
            buf.Glyphs[i] = g;
        }
        int t = g.AttachTo;
        if (t >= 0 && t < buf.Count && t != i && state[t] != 1)
        {
            Resolve(buf, t, state);
            var target = buf.Glyphs[t];
            // mark origin = target origin + anchor delta, expressed relative to the mark's own pen position
            int pen = 0;
            if (t < i) { for (int k = t; k < i; k++) pen -= buf.Glyphs[k].XAdvance; }
            else { for (int k = i; k < t; k++) pen += buf.Glyphs[k].XAdvance; }
            g.XOffset = target.XOffset + g.AttachDx + pen;
            g.YOffset = target.YOffset + g.AttachDy;
            buf.Glyphs[i] = g;
        }
        state[i] = 2;
    }

    // ── per-run shaping ─────────────────────────────────────────────────────────

    private static GlyphBuffer ShapeRun(FontData font, ScriptKind kind, IndicScriptInfo? indic,
        List<int> cps, List<int> clusters)
    {
        var ot = font.OpenTypeLayout;
        var buf = new GlyphBuffer();

        string[] scriptTags;
        List<IndicChar>? indicChars = null;
        switch (kind)
        {
            case ScriptKind.Thai:
                PreprocessThai(cps, clusters);
                scriptTags = cps.Count > 0 && cps[0] >= 0x0E80 ? new[] { "lao ", "DFLT" } : new[] { "thai", "DFLT" };
                break;
            case ScriptKind.Indic when indic != null:
                indicChars = IndicSyllables.Prepare(indic, cps, clusters);
                scriptTags = new string[indic.Tags.Length + 1];
                Array.Copy(indic.Tags, scriptTags, indic.Tags.Length);
                scriptTags[scriptTags.Length - 1] = "DFLT";
                break;
            case ScriptKind.Arabic:
                scriptTags = new[] { "arab", "DFLT" };
                break;
            case ScriptKind.Tibetan:
                scriptTags = new[] { "tibt", "DFLT" };
                break;
            case ScriptKind.Khmer:
                ReorderKhmer(cps, clusters);
                scriptTags = new[] { "khmr", "DFLT" };
                break;
            case ScriptKind.Myanmar:
                ReorderMyanmar(cps, clusters);
                scriptTags = new[] { "mym2", "mymr", "DFLT" };
                break;
            default:
                scriptTags = new[] { "latn", "DFLT" };
                break;
        }

        bool positionalForms = false;
        int count = indicChars?.Count ?? cps.Count;
        for (int i = 0; i < count; i++)
        {
            int cp = indicChars != null ? indicChars[i].Cp : cps[i];
            int cluster = indicChars != null ? indicChars[i].Cluster : clusters[i];
            ushort gid = font.GetGlyphId(cp);

            // Arabic letter shaped to a presentation form the font lacks: use the base letter(s)
            // and let the font's own isol/init/medi/fina features select the positional glyph.
            if (kind == ScriptKind.Arabic && gid == 0 &&
                ArabicShaper.TryGetFormInfo(cp, out var formBase, out var forms))
            {
                for (int k = 0; k < formBase.Length; k++)
                {
                    uint formMask = forms[k] == ArabicShaper.ArabicForm.Isolated ? MaskIsol
                        : forms[k] == ArabicShaper.ArabicForm.Final ? MaskFina
                        : forms[k] == ArabicShaper.ArabicForm.Initial ? MaskInit : MaskMedi;
                    buf.Glyphs.Add(new ShapedGlyph
                    {
                        Id = font.GetGlyphId(formBase[k]), Cluster = cluster, Codepoint = formBase[k],
                        AttachTo = -1, LigComp = -1, Mask = MaskGlobal | formMask, Syl = -1,
                    });
                }
                positionalForms = true;
                continue;
            }

            if (gid == 0 && IsDefaultIgnorable(cp)) continue;
            var g = new ShapedGlyph
            {
                Id = gid, Cluster = cluster, Codepoint = cp, AttachTo = -1, LigComp = -1,
                Mask = MaskGlobal, IsMarkHint = IsMarkChar(cp), Syl = -1,
            };
            if (indicChars != null)
            {
                g.Role = indicChars[i].Role;
                g.Syl = indicChars[i].Syl;
                if (g.Role == IndicRole.Reph) g.Mask |= MaskReph;
                if (g.Role >= IndicRole.MatraAboveBelow && g.Role <= IndicRole.Modifier) g.IsMarkHint = g.IsMarkHint || g.Role == IndicRole.Modifier;
            }
            buf.Glyphs.Add(g);
        }

        var gsub = ot.Gsub;
        var script = gsub?.Layout.FindScript(scriptTags);
        var ls = script?.Default;
        if (gsub != null && ls != null)
        {
            if (kind == ScriptKind.Indic)
            {
                ApplyStage(gsub, ls, buf, new[] { "locl", "ccmp" }, uint.MaxValue);
                foreach (var f in new[] { "nukt", "akhn" })
                    ApplyStage(gsub, ls, buf, new[] { f }, uint.MaxValue);
                ApplyStage(gsub, ls, buf, new[] { "rphf" }, MaskReph);
                if (indic != null && indic.HasReph)
                    MarkFormedRephs(font, indic, buf);
                foreach (var f in new[] { "rkrf", "pref", "blwf", "abvf", "half", "pstf", "vatu", "cjct" })
                    ApplyStage(gsub, ls, buf, new[] { f }, uint.MaxValue);
                if (indic != null && indic.HasReph)
                    FinalizeReph(font, indic, buf, ot.Gdef);
                ApplyStage(gsub, ls, buf, new[] { "init", "pres", "abvs", "blws", "psts", "haln", "calt", "clig" }, uint.MaxValue);
            }
            else if (kind == ScriptKind.Khmer || kind == ScriptKind.Myanmar)
            {
                ApplySoutheastAsianFeatures(kind, gsub, ls, buf);
            }
            else
            {
                ApplyStage(gsub, ls, buf, new[] { "ccmp", "locl" }, uint.MaxValue);
                if (positionalForms)
                {
                    ApplyStage(gsub, ls, buf, new[] { "isol" }, MaskIsol);
                    ApplyStage(gsub, ls, buf, new[] { "fina" }, MaskFina);
                    ApplyStage(gsub, ls, buf, new[] { "medi" }, MaskMedi);
                    ApplyStage(gsub, ls, buf, new[] { "init" }, MaskInit);
                }
                ApplyStage(gsub, ls, buf, new[] { "rlig", "calt", "rclt", "clig", "liga", "mset" }, uint.MaxValue);
            }
        }
        else if (kind == ScriptKind.Indic && indic != null && indic.HasReph)
        {
            FinalizeReph(font, indic, buf, ot.Gdef);
        }

        for (int i = 0; i < buf.Count; i++)
        {
            var g = buf.Glyphs[i];
            g.XAdvance = font.GetAdvanceWidth(g.Id);
            buf.Glyphs[i] = g;
        }

        var gpos = ot.Gpos;
        var gposScript = gpos?.Layout.FindScript(scriptTags);
        if (gpos != null && gposScript?.Default != null)
        {
            gpos.Rtl = kind == ScriptKind.Arabic;
            ApplyStage(gpos, gposScript.Default, buf, new[] { "curs", "kern", "dist", "mark", "mkmk", "abvm", "blwm" }, uint.MaxValue);
        }

        return buf;
    }

    private static void ApplyStage(OtEngine eng, OtLangSys ls, GlyphBuffer buf, string[] tags, uint mask)
    {
        var lookups = new List<int>();
        foreach (var tag in tags)
            foreach (var li in eng.Layout.LookupsFor(ls, tag))
                if (!lookups.Contains(li)) lookups.Add(li);
        lookups.Sort();
        foreach (var li in lookups) eng.ApplyLookup(li, buf, mask);
    }

    // ── script specifics ───────────────────────────────────────────────────────

    private static bool IsThaiTone(int cp) => (cp >= 0x0E48 && cp <= 0x0E4B) || (cp >= 0x0EC8 && cp <= 0x0ECB);

    /// <summary>Decompose SARA AM into NIKHAHIT + SARA AA, placing the nikhahit before any tone mark.</summary>
    private static void PreprocessThai(List<int> cps, List<int> clusters)
    {
        var outCps = new List<int>(cps.Count + 2);
        var outClusters = new List<int>(cps.Count + 2);
        for (int i = 0; i < cps.Count; i++)
        {
            int cp = cps[i];
            if (cp == 0x0E33 || cp == 0x0EB3)
            {
                int nik = cp == 0x0E33 ? 0x0E4D : 0x0ECD;
                int aa = cp == 0x0E33 ? 0x0E32 : 0x0EB2;
                int last = outCps.Count - 1;
                if (last >= 0 && IsThaiTone(outCps[last]))
                {
                    outCps.Insert(last, nik);
                    outClusters.Insert(last, clusters[i]);
                }
                else
                {
                    outCps.Add(nik);
                    outClusters.Add(clusters[i]);
                }
                outCps.Add(aa);
                outClusters.Add(clusters[i]);
            }
            else
            {
                outCps.Add(cp);
                outClusters.Add(clusters[i]);
            }
        }
        cps.Clear(); cps.AddRange(outCps);
        clusters.Clear(); clusters.AddRange(outClusters);
    }

    /// <summary>
    /// After the rphf feature has turned a syllable-initial RA+virama into a reph glyph, move it
    /// behind the base consonant (and any above/below marks), before right-side matras.
    /// A reph that was not formed (glyph unchanged) stays put as an ordinary half-RA.
    /// </summary>
    private static void FinalizeReph(FontData font, IndicScriptInfo indic, GlyphBuffer buf, Gdef? gdef)
    {
        int i = 0;
        while (i < buf.Count)
        {
            int syl = buf.Glyphs[i].Syl;
            int start = i;
            while (i < buf.Count && buf.Glyphs[i].Syl == syl && syl >= 0) i++;
            if (i == start) { i++; continue; }
            int end = i;

            var head = buf.Glyphs[start];
            if (head.Role != IndicRole.Reph || (head.Mask & MaskRephFormed) == 0) continue;

            int baseIdx = -1;
            for (int k = end - 1; k > start; k--)
            {
                byte r = buf.Glyphs[k].Role;
                if (r == IndicRole.Base || r == IndicRole.Consonant) { baseIdx = k; break; }
            }
            if (baseIdx < 0) continue;

            int moveCount = 1;
            while (moveCount < 3 && start + moveCount < end && buf.Glyphs[start + moveCount].Role == IndicRole.RephHalant)
                moveCount++;

            int dest = RephDestination(indic.RephPos, buf, start, baseIdx, end);

            var moving = buf.Glyphs.GetRange(start, moveCount);
            buf.Glyphs.RemoveRange(start, moveCount);
            buf.Glyphs.InsertRange(dest - moveCount, moving);
        }
    }

    /// <summary>
    /// Flag the RA glyphs the rphf feature actually turned into a reph. Only those are moved
    /// later: another feature may also rewrite RA (e.g. Malayalam's chillu), which must stay put.
    /// </summary>
    private static void MarkFormedRephs(FontData font, IndicScriptInfo indic, GlyphBuffer buf)
    {
        for (int i = 0; i < buf.Count; i++)
        {
            var g = buf.Glyphs[i];
            if (g.Role == IndicRole.Reph && g.Id != font.GetGlyphId(g.Codepoint))
            {
                g.Mask |= MaskRephFormed;
                buf.Glyphs[i] = g;
            }
        }
    }

    /// <summary>
    /// Index (in the pre-removal buffer) where a reph is inserted, following each script's reph
    /// position: right after the base, after above/below marks, after below-base consonant forms,
    /// or at the end of the syllable before its trailing modifiers.
    /// </summary>
    private static int RephDestination(RephPosition pos, GlyphBuffer buf, int start, int baseIdx, int end)
    {
        int dest = baseIdx + 1;
        switch (pos)
        {
            case RephPosition.AfterMain:
                while (dest < end && buf.Glyphs[dest].Role == IndicRole.Nukta) dest++;
                break;
            case RephPosition.BeforeSub:
                while (dest < end && (buf.Glyphs[dest].Role == IndicRole.Nukta || buf.Glyphs[dest].Role == IndicRole.MatraAboveBelow)) dest++;
                break;
            case RephPosition.AfterSub:
            case RephPosition.BeforePost:
                while (dest < end)
                {
                    byte r = buf.Glyphs[dest].Role;
                    if (r == IndicRole.Nukta || r == IndicRole.MatraAboveBelow || r == IndicRole.Halant ||
                        r == IndicRole.Consonant || r == IndicRole.Joiner) dest++;
                    else break;
                }
                break;
            case RephPosition.AfterPost:
                dest = end;
                while (dest > baseIdx + 1 && buf.Glyphs[dest - 1].Role == IndicRole.Modifier) dest--;
                break;
        }
        return dest;
    }

    private static bool IsMarkChar(int cp)
    {
        if (cp > 0xFFFF) return false;
        var cat = char.GetUnicodeCategory((char)cp);
        return cat == System.Globalization.UnicodeCategory.NonSpacingMark ||
               cat == System.Globalization.UnicodeCategory.EnclosingMark;
    }

    private static bool IsDefaultIgnorable(int cp)
        => (cp >= 0x200B && cp <= 0x200F) || (cp >= 0x2060 && cp <= 0x206F) || cp == 0xFEFF || cp == 0x00AD;
}
