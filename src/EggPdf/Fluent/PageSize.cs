namespace EggPdf.Fluent;

/// <summary>A page's physical dimensions in millimeters, matching CSS <c>@page { size: ... }</c>.</summary>
public readonly struct PageSize
{
    /// <summary>Page width in millimeters.</summary>
    public float WidthMm { get; }

    /// <summary>Page height in millimeters.</summary>
    public float HeightMm { get; }

    /// <summary>A custom page size in millimeters.</summary>
    public PageSize(float widthMm, float heightMm)
    {
        WidthMm = widthMm;
        HeightMm = heightMm;
    }

    /// <summary>A3 portrait (297 x 420 mm).</summary>
    public static readonly PageSize A3 = new PageSize(297f, 420f);

    /// <summary>A4 portrait (210 x 297 mm) -- the default.</summary>
    public static readonly PageSize A4 = new PageSize(210f, 297f);

    /// <summary>A5 portrait (148 x 210 mm).</summary>
    public static readonly PageSize A5 = new PageSize(148f, 210f);

    /// <summary>US Letter portrait (8.5 x 11 in).</summary>
    public static readonly PageSize Letter = new PageSize(215.9f, 279.4f);

    /// <summary>US Legal portrait (8.5 x 14 in).</summary>
    public static readonly PageSize Legal = new PageSize(215.9f, 355.6f);

    /// <summary>Same physical page, rotated: width and height swapped.</summary>
    public PageSize Landscape() => new PageSize(HeightMm, WidthMm);
}
