using System;
using System.Collections.Generic;

namespace EggPdf.Pdf;

/// <summary>
/// PDF file attachments for embedding files within a PDF document.
/// Supports ZUGFeRD/Factur-X e-invoicing (XML invoice data embedded in PDF/A-3).
/// </summary>
public class PdfAttachment
{
    /// <summary>File name displayed in the PDF viewer.</summary>
    public string FileName { get; set; } = "";

    /// <summary>File data bytes.</summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>MIME type of the attachment.</summary>
    public string MimeType { get; set; } = "application/octet-stream";

    /// <summary>Description of the attachment.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// AF relationship type for PDF/A-3 compliance.
    /// "Alternative" for ZUGFeRD/Factur-X (XML is an alternative representation).
    /// "Data" for supplementary data files.
    /// "Source" for source files.
    /// </summary>
    public string Relationship { get; set; } = "Alternative";

    /// <summary>Creation date.</summary>
    public DateTime CreationDate { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Collection of PDF attachments to be embedded in the document.
/// </summary>
public class PdfAttachmentCollection
{
    private readonly List<PdfAttachment> _attachments = new();

    /// <summary>Add a file attachment.</summary>
    public PdfAttachmentCollection Add(string fileName, byte[] data, string mimeType = "application/octet-stream",
        string relationship = "Alternative", string? description = null)
    {
        _attachments.Add(new PdfAttachment
        {
            FileName = fileName,
            Data = data,
            MimeType = mimeType,
            Relationship = relationship,
            Description = description,
        });
        return this;
    }

    /// <summary>Add a ZUGFeRD/Factur-X XML invoice attachment.</summary>
    public PdfAttachmentCollection AddZugferd(byte[] xmlData, string profile = "BASIC")
    {
        return Add(
            fileName: "factur-x.xml",
            data: xmlData,
            mimeType: "text/xml",
            relationship: "Alternative",
            description: $"Factur-X {profile} invoice data"
        );
    }

    /// <summary>Get all attachments.</summary>
    public IReadOnlyList<PdfAttachment> Attachments => _attachments;

    /// <summary>Check if there are any attachments.</summary>
    public bool HasAttachments => _attachments.Count > 0;
}
