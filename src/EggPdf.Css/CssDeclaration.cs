namespace EggPdf.Css;

/// <summary>
/// A single CSS property declaration (e.g., "color: red !important").
/// </summary>
public class CssDeclaration
{
    public string Property { get; }
    public string Value { get; }
    public bool Important { get; }

    public CssDeclaration(string property, string value, bool important = false)
    {
        Property = property;
        Value = value;
        Important = important;
    }
}
