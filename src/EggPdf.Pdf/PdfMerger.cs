using System;
using System.Collections.Generic;
using System.IO;

namespace EggPdf.Pdf;

/// <summary>
/// Merges multiple PDF documents into a single PDF. Every page is copied with everything it uses -- content
/// streams, fonts, images, other resources and its annotations -- and streams are copied as stored, still
/// compressed. The inputs' outlines, form fields, structure trees and page labels are not carried over.
/// </summary>
public class PdfMerger
{
    private readonly List<MergeInput> _inputs = new();

    /// <summary>Add a PDF document to the merge queue.</summary>
    public PdfMerger Add(byte[] pdfBytes)
    {
        if (pdfBytes != null && pdfBytes.Length > 0)
            _inputs.Add(new MergeInput(pdfBytes, null));
        return this;
    }

    /// <summary>Add a PDF document from a file path; the file is read when the merge runs.</summary>
    public PdfMerger AddFile(string filePath)
    {
        if (File.Exists(filePath))
            _inputs.Add(new MergeInput(null, filePath));
        return this;
    }

    /// <summary>
    /// Merge all added documents into a single PDF. A single in-memory input is returned unchanged.
    /// </summary>
    /// <exception cref="NotSupportedException">An input is encrypted.</exception>
    public byte[] Build()
    {
        if (_inputs.Count == 0)
            return Array.Empty<byte>();
        if (_inputs.Count == 1 && _inputs[0].Bytes != null)
            return _inputs[0].Bytes!;

        using var output = new MemoryStream();
        WriteTo(output);
        return output.ToArray();
    }

    /// <summary>Merge all added documents and write the merged PDF to <paramref name="output"/>.</summary>
    /// <exception cref="NotSupportedException">An input is encrypted.</exception>
    public void WriteTo(Stream output)
    {
        if (output == null) throw new ArgumentNullException(nameof(output));

        var writer = new PdfMergeWriter(output);
        foreach (var input in _inputs)
        {
            if (input.Bytes != null && PdfInspect.IsEncrypted(input.Bytes))
                throw EncryptedInput();
            using var reader = input.Open();
            if (reader.IsEncrypted)
                throw EncryptedInput();
            writer.AddPages(reader);
        }
        writer.Finish();
    }

    private static NotSupportedException EncryptedInput()
        => new("Merging encrypted PDFs is not supported — decrypt or re-render the input first.");

    private sealed class MergeInput
    {
        public MergeInput(byte[]? bytes, string? path)
        {
            Bytes = bytes;
            Path = path;
        }

        public byte[]? Bytes { get; }

        public string? Path { get; }

        public PdfReader Open() => Bytes != null ? PdfReader.Open(Bytes) : PdfReader.Open(Path!);
    }
}
