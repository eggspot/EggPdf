using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EggPdf.Pdf;

/// <summary>
/// PDF AcroForm support: generates fillable form fields from HTML form elements.
/// Converts input, select, textarea, button elements to PDF form widgets.
/// </summary>
public class PdfForm
{
    private readonly List<PdfFormField> _fields = new();

    /// <summary>Add a text input field.</summary>
    public void AddTextField(string name, string value, float x, float y, float width, float height)
    {
        _fields.Add(new PdfFormField
        {
            Type = FormFieldType.Text,
            Name = name,
            Value = value,
            X = x, Y = y, Width = width, Height = height,
        });
    }

    /// <summary>Add a checkbox field.</summary>
    public void AddCheckbox(string name, bool isChecked, float x, float y, float size)
    {
        _fields.Add(new PdfFormField
        {
            Type = FormFieldType.Checkbox,
            Name = name,
            Value = isChecked ? "Yes" : "Off",
            X = x, Y = y, Width = size, Height = size,
        });
    }

    /// <summary>Add a dropdown (choice) field.</summary>
    public void AddDropdown(string name, string selectedValue, string[] options, float x, float y, float width, float height)
    {
        _fields.Add(new PdfFormField
        {
            Type = FormFieldType.Choice,
            Name = name,
            Value = selectedValue,
            Options = options,
            X = x, Y = y, Width = width, Height = height,
        });
    }

    /// <summary>Add a button field.</summary>
    public void AddButton(string name, string label, float x, float y, float width, float height)
    {
        _fields.Add(new PdfFormField
        {
            Type = FormFieldType.Button,
            Name = name,
            Value = label,
            X = x, Y = y, Width = width, Height = height,
        });
    }

    /// <summary>Get all form fields.</summary>
    public IReadOnlyList<PdfFormField> Fields => _fields;

    /// <summary>Check if there are any form fields.</summary>
    public bool HasFields => _fields.Count > 0;
}

/// <summary>A single PDF form field.</summary>
public class PdfFormField
{
    public FormFieldType Type { get; set; }
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string[]? Options { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
}

/// <summary>Types of PDF form fields.</summary>
public enum FormFieldType
{
    Text,
    Checkbox,
    Choice,
    Button,
}
