using System.Collections.Generic;
using EggPdf.Css;

namespace EggPdf.Layout;

public static partial class BlockLayout
{
    /// <summary>The inherited properties <c>::first-line</c> can change on the content of the line.</summary>
    private static readonly string[] FirstLineInheritedProperties =
    {
        "color", "font-family", "font-size", "font-style", "font-weight", "font-variant", "font-stretch",
        "letter-spacing", "word-spacing", "text-transform", "line-height", "text-shadow",
    };

    /// <summary>
    /// <c>::first-line</c> for a block whose inline content is laid out word by word: the words that land on the
    /// block's first line take the pseudo-element's style. It acts as an inline parent of everything on the line, so
    /// text directly in the block takes it whole, while an inline element (<c>&lt;b&gt;</c>, <c>&lt;a&gt;</c>, ...)
    /// keeps the properties it declares itself and takes the rest from the first line.
    /// </summary>
    private sealed class FirstLineFormatting
    {
        private readonly ComputedStyle _blockStyle;
        private readonly ComputedStyle _firstLineStyle;
        private readonly float _blockFontSize;
        private readonly float _lineY;
        private readonly Dictionary<ComputedStyle, ComputedStyle> _mergedStyles = new();
        private bool _ended;

        public FirstLineFormatting(ComputedStyle blockStyle, ComputedStyle firstLineStyle, float blockFontSize, float lineY)
        {
            _blockStyle = blockStyle;
            _firstLineStyle = firstLineStyle;
            _blockFontSize = blockFontSize;
            _lineY = lineY;
        }

        /// <summary>Whether content placed at <paramref name="childY"/> is on the first line; once a later line starts, never again.</summary>
        public bool IsOnFirstLine(float childY)
        {
            if (_ended) return false;
            if (childY == _lineY) return true;
            _ended = true;
            return false;
        }

        /// <summary>The style of a run's words while they are on the first line.</summary>
        public ComputedStyle StyleFor(ComputedStyle runStyle)
        {
            if (ReferenceEquals(runStyle, _blockStyle)) return _firstLineStyle;
            if (_mergedStyles.TryGetValue(runStyle, out var merged)) return merged;

            merged = new ComputedStyle();
            foreach (var kv in runStyle.All) merged.Set(kv.Key, kv.Value);
            foreach (var property in FirstLineInheritedProperties)
            {
                var lineValue = _firstLineStyle.Get(property);
                var blockValue = _blockStyle.Get(property);
                if (lineValue == null || lineValue == blockValue) continue;
                if (runStyle.Get(property) == blockValue) merged.Set(property, lineValue);
            }
            _mergedStyles[runStyle] = merged;
            return merged;
        }

        /// <summary>
        /// The style a run's next word at <paramref name="childY"/> should use, when it differs from
        /// <paramref name="activeStyle"/>: the first-line style while on the first line, the run's own afterwards.
        /// </summary>
        public bool TryChangeStyle(float childY, ComputedStyle runStyle, float runFontSize, ComputedStyle activeStyle,
            out ComputedStyle style, out float fontSize)
        {
            if (IsOnFirstLine(childY))
            {
                style = StyleFor(runStyle);
                fontSize = FontSizeFor(style, runStyle, runFontSize);
            }
            else
            {
                style = runStyle;
                fontSize = runFontSize;
            }
            return !ReferenceEquals(style, activeStyle);
        }

        /// <summary>The font size in pixels for a first-line style; relative sizes resolve against the block's.</summary>
        private float FontSizeFor(ComputedStyle lineStyle, ComputedStyle runStyle, float runFontSize) =>
            lineStyle.FontSize == runStyle.FontSize ? runFontSize : ResolveFontSize(lineStyle.FontSize, _blockFontSize);
    }
}
