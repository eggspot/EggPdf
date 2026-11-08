using System.Collections.Generic;

namespace EggPdf.Svg;

/// <summary>The output of <see cref="SvgRenderer.RenderWithResources"/>.</summary>
public sealed class SvgRenderResult
{
    /// <summary>Create a result.</summary>
    public SvgRenderResult(string commands, List<string> usedImages, List<string> usedFonts, List<string> usedExtGStates)
    {
        Commands = commands;
        UsedImages = usedImages;
        UsedFonts = usedFonts;
        UsedExtGStates = usedExtGStates;
    }

    /// <summary>The PDF content stream fragment that draws the SVG.</summary>
    public string Commands { get; }

    /// <summary>Names of the image XObjects the fragment draws; register them on the page.</summary>
    public List<string> UsedImages { get; }

    /// <summary>Names of the standard PDF fonts the fragment's text uses; register them on the page.</summary>
    public List<string> UsedFonts { get; }

    /// <summary>Names of the graphics states (opacity) the fragment selects; register them on the page.</summary>
    public List<string> UsedExtGStates { get; }
}
