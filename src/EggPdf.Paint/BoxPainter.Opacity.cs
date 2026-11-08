using System;
using System.Globalization;
using EggPdf.Layout;
using EggPdf.Pdf;

namespace EggPdf.Paint;

public static partial class BoxPainter
{
    [ThreadStatic]
    private static float _boxOpacity;

    [ThreadStatic]
    private static bool _insideOpacityScope;

    private static void PaintBoxWithOpacity(PdfPage page, LayoutBox box, float pageHeightPt, float pageHeightPx, float adjustedY)
    {
        float opacity = box.AncestorOpacity * OwnOpacity(box);
        float previousOpacity = _boxOpacity;
        bool previousScope = _insideOpacityScope;
        _boxOpacity = opacity;
        _insideOpacityScope = true;
        try
        {
            if (opacity < 1f) page.SetOpacity(opacity);
            PaintBoxCore(page, box, pageHeightPt, pageHeightPx, adjustedY);
            if (opacity < 1f) page.SetOpacity(1f);
        }
        finally
        {
            _boxOpacity = previousOpacity;
            _insideOpacityScope = previousScope;
        }
    }

    private static float CurrentBoxOpacity => _insideOpacityScope ? _boxOpacity : 1f;

    private static void SetPaintOpacity(PdfPage page, float alpha) => page.SetOpacity(alpha * CurrentBoxOpacity);

    private static float OwnOpacity(LayoutBox box)
    {
        float opacity = 1f;
        var value = box.Style.Get("opacity");
        if (!string.IsNullOrEmpty(value))
        {
            var trimmed = value!.Trim();
            bool percent = trimmed.EndsWith("%", StringComparison.Ordinal);
            if (percent) trimmed = trimmed.Substring(0, trimmed.Length - 1);
            if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) && !float.IsNaN(parsed))
                opacity = Math.Max(0f, Math.Min(1f, percent ? parsed / 100f : parsed));
        }
        var filter = box.Style.Get("filter");
        if (!string.IsNullOrEmpty(filter) && filter != "none")
            opacity *= PdfFilterEffects.Parse(filter!).Opacity;
        return opacity;
    }
}
