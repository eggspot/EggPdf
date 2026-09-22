using System;
using System.Collections.Generic;
using System.Globalization;
using EggPdf.Core;

namespace EggPdf.Svg;

public static partial class SvgRenderer
{
    /// <summary>
    /// A linearGradient/radialGradient resolved for one shape: units, transform, spread and stops (attributes and
    /// stops are inherited along the href chain). Colour is evaluated per pixel while the filter source is drawn.
    /// </summary>
    private sealed class GradientPaint
    {
        private bool _radial, _bboxUnits = true;
        private float _x1, _y1, _x2 = 1f, _y2, _cx = 0.5f, _cy = 0.5f, _r = 0.5f;
        private Matrix2D _inverseTransform = new Matrix2D(1, 0, 0, 1, 0, 0);
        private string _spread = "pad";
        private readonly List<(float offset, float r, float g, float b, float a)> _stops = new List<(float, float, float, float, float)>();
        private float _boxX, _boxY, _boxW, _boxH;

        public static GradientPaint? TryCreate(string? id, SvgDefs defs, FilterShape shape)
        {
            if (id == null || !defs.Ids.TryGetValue(id, out var element)) return null;
            if (element.TagName != "lineargradient" && element.TagName != "radialgradient") return null;

            var chain = new List<SvgElement>();
            for (var current = element; current != null && chain.Count < 8;)
            {
                chain.Add(current);
                var href = current.GetAttribute("href");
                if (href.Length == 0) href = current.GetAttribute("xlink:href");
                current = href.Length > 1 && href[0] == '#' && defs.Ids.TryGetValue(href.Substring(1), out var next)
                    && (next.TagName == "lineargradient" || next.TagName == "radialgradient") && !chain.Contains(next) ? next : null;
            }

            var g = new GradientPaint { _radial = element.TagName == "radialgradient", _boxX = shape.BoxX, _boxY = shape.BoxY, _boxW = shape.BoxW, _boxH = shape.BoxH };
            string Attr(string name)
            {
                foreach (var e in chain) { var v = e.GetAttribute(name); if (v.Length > 0) return v; }
                return "";
            }

            g._bboxUnits = !string.Equals(Attr("gradientunits"), "userSpaceOnUse", StringComparison.OrdinalIgnoreCase);
            g._spread = Attr("spreadmethod").Trim().ToLowerInvariant();
            var transform = Attr("gradienttransform");
            if (transform.Length > 0) g._inverseTransform = InvertMatrix(ParseTransformToMatrix(transform));

            if (g._radial)
            {
                g._cx = Unit(Attr("cx"), 0.5f); g._cy = Unit(Attr("cy"), 0.5f); g._r = Unit(Attr("r"), 0.5f);
            }
            else
            {
                g._x1 = Unit(Attr("x1"), 0f); g._y1 = Unit(Attr("y1"), 0f);
                g._x2 = Unit(Attr("x2"), 1f); g._y2 = Unit(Attr("y2"), 0f);
            }

            foreach (var e in chain)
            {
                foreach (var stop in e.Children)
                {
                    if (stop.TagName != "stop") continue;
                    var color = Color.TryParse(SvgFilter.Prop(stop, "stop-color")) ?? Color.Black;
                    float opacity = ReadFraction(SvgFilter.Prop(stop, "stop-opacity"), 1f) * color.A / 255f;
                    float offset = Math.Max(g._stops.Count > 0 ? g._stops[g._stops.Count - 1].offset : 0f, Math.Min(1f, Unit(stop.GetAttribute("offset"), 0f)));
                    g._stops.Add((offset, color.R / 255f, color.G / 255f, color.B / 255f, opacity));
                }
                if (g._stops.Count > 0) break; // stops come from the nearest gradient that has any
            }
            return g._stops.Count == 0 ? null : g;
        }

        /// <summary>A number, or a percentage as a fraction; used for coordinates in both unit systems.</summary>
        private static float Unit(string value, float fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            var v = value.Trim();
            bool percent = v.EndsWith("%", StringComparison.Ordinal);
            if (percent) v = v.Substring(0, v.Length - 1);
            else if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase)) v = v.Substring(0, v.Length - 2);
            return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) ? (percent ? n / 100f : n) : fallback;
        }

        /// <summary>The colour at a point of the shape's own user space.</summary>
        public (byte r, byte g, byte b, float a) ColorAt(float ux, float uy)
        {
            float gx = ux, gy = uy;
            if (_bboxUnits)
            {
                gx = _boxW > 0f ? (ux - _boxX) / _boxW : 0f;
                gy = _boxH > 0f ? (uy - _boxY) / _boxH : 0f;
            }
            var (tx, ty) = _inverseTransform.Apply(gx, gy);

            float t;
            if (_radial)
            {
                float dx = tx - _cx, dy = ty - _cy;
                t = _r > 0f ? (float)Math.Sqrt(dx * dx + dy * dy) / _r : 0f;
            }
            else
            {
                float vx = _x2 - _x1, vy = _y2 - _y1, len2 = vx * vx + vy * vy;
                t = len2 > 0f ? ((tx - _x1) * vx + (ty - _y1) * vy) / len2 : 0f;
            }
            t = Spread(t);

            // Interpolate between the two stops around t (colours are straight, sRGB)
            var first = _stops[0];
            if (t <= first.offset) return Pack(first.r, first.g, first.b, first.a);
            for (int i = 1; i < _stops.Count; i++)
            {
                var a = _stops[i - 1]; var b = _stops[i];
                if (t > b.offset) continue;
                float span = b.offset - a.offset;
                float f = span > 0f ? (t - a.offset) / span : 1f;
                return Pack(a.r + (b.r - a.r) * f, a.g + (b.g - a.g) * f, a.b + (b.b - a.b) * f, a.a + (b.a - a.a) * f);
            }
            var last = _stops[_stops.Count - 1];
            return Pack(last.r, last.g, last.b, last.a);
        }

        private float Spread(float t)
        {
            if (_spread == "repeat") return t - (float)Math.Floor(t);
            if (_spread == "reflect")
            {
                float m = t - 2f * (float)Math.Floor(t / 2f);
                return m > 1f ? 2f - m : m;
            }
            return t < 0f ? 0f : t > 1f ? 1f : t;
        }

        private static (byte, byte, byte, float) Pack(float r, float g, float b, float a)
            => ((byte)Math.Round(r * 255f), (byte)Math.Round(g * 255f), (byte)Math.Round(b * 255f), a);
    }
}
