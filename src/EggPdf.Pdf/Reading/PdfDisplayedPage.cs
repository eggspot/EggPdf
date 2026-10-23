namespace EggPdf.Pdf;

internal readonly struct PdfDisplayedPage
{
    public PdfDisplayedPage(PdfPageNode page)
    {
        var box = page.VisibleBox;
        double boxWidth = box[2] - box[0], boxHeight = box[3] - box[1];
        Rotation = page.Rotation;
        Height = Rotation % 180 == 0 ? boxHeight : boxWidth;
        ToPage = Rotation switch
        {
            90 => new[] { 0.0, 1, -1, 0, boxWidth, 0 },
            180 => new[] { -1.0, 0, 0, -1, boxWidth, boxHeight },
            270 => new[] { 0.0, -1, 1, 0, 0, boxHeight },
            _ => new[] { 1.0, 0, 0, 1, 0, 0 },
        };
        ToPage[4] += box[0];
        ToPage[5] += box[1];
    }

    public int Rotation { get; }

    public double Height { get; }

    public double[] ToPage { get; }

    public double[] PageRectangle(double left, double bottom, double width, double height)
    {
        double ax = ToPage[0] * left + ToPage[2] * bottom + ToPage[4];
        double ay = ToPage[1] * left + ToPage[3] * bottom + ToPage[5];
        double bx = ToPage[0] * (left + width) + ToPage[2] * (bottom + height) + ToPage[4];
        double by = ToPage[1] * (left + width) + ToPage[3] * (bottom + height) + ToPage[5];
        return new[] { System.Math.Min(ax, bx), System.Math.Min(ay, by), System.Math.Max(ax, bx), System.Math.Max(ay, by) };
    }
}
