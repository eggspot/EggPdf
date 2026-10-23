using System;

namespace EggPdf.Pdf;

/// <summary>
/// The bytes aren't a PDF EggPdf can read: not a PDF at all, damaged beyond repair, or over a safety limit
/// (nesting depth, page-tree depth, decompressed stream size, cross-reference size).
/// </summary>
public class PdfFormatException : FormatException
{
    /// <summary>Creates the exception with a message describing what was wrong.</summary>
    public PdfFormatException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the error that caused it.</summary>
    public PdfFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
