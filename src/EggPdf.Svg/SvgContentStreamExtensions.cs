using System.Text;

namespace EggPdf.Svg;

internal static class SvgContentStreamExtensions
{
    public static StringBuilder AppendOpLine(this StringBuilder sb, string value) => sb.Append(value).Append('\n');

    public static StringBuilder AppendOpLine(this StringBuilder sb) => sb.Append('\n');
}
