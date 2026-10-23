using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using EggPdf.Pdf;

namespace EggPdf.Cli;

internal sealed class CliSigning : IDisposable
{
    public const string PfxOption = "--sign-pfx";
    public const string PfxPasswordEnvOption = "--sign-password-env";
    public const string CertificateOption = "--sign-cert";
    public const string KeyOption = "--sign-key";
    public const string KeyPasswordEnvOption = "--sign-key-password-env";
    public const string ReasonOption = "--sign-reason";
    public const string LocationOption = "--sign-location";
    public const string NameOption = "--sign-name";
    public const string FieldOption = "--sign-field";
    public const string ImageOption = "--sign-image";
    public const string PageOption = "--sign-page";
    public const string BoxOption = "--sign-box";

    private readonly X509Certificate2 _certificate;
    private readonly PdfSigner.SignOptions _options;

    private CliSigning(X509Certificate2 certificate, PdfSigner.SignOptions options)
    {
        _certificate = certificate;
        _options = options;
    }

    public string SignerName => _options.Name ?? _certificate.GetNameInfo(X509NameType.SimpleName, false);

    public static CliSigning? FromOptions(IReadOnlyDictionary<string, string> values)
    {
        values.TryGetValue(PfxOption, out var pfxPath);
        values.TryGetValue(CertificateOption, out var certificatePath);
        values.TryGetValue(KeyOption, out var keyPath);
        if (pfxPath == null && certificatePath == null && keyPath == null)
        {
            RejectSigningDetailsWithoutCertificate(values);
            return null;
        }

        if (pfxPath != null && (certificatePath != null || keyPath != null))
            throw new ArgumentException($"Use either {PfxOption} or {CertificateOption} with {KeyOption}, not both.");
        if (pfxPath == null && (certificatePath == null || keyPath == null))
            throw new ArgumentException($"{CertificateOption} and {KeyOption} must be given together.");
        if (pfxPath != null && values.ContainsKey(KeyPasswordEnvOption))
            throw new ArgumentException($"{KeyPasswordEnvOption} goes with {KeyOption}; for {PfxOption} use {PfxPasswordEnvOption}.");
        if (pfxPath == null && values.ContainsKey(PfxPasswordEnvOption))
            throw new ArgumentException($"{PfxPasswordEnvOption} goes with {PfxOption}; for {KeyOption} use {KeyPasswordEnvOption}.");

        var certificate = pfxPath != null
            ? PdfSigningCertificate.FromPfx(ReadFile(pfxPath), PasswordFromEnvironment(values, PfxPasswordEnvOption))
            : PdfSigningCertificate.FromPem(File.ReadAllText(RequireFile(certificatePath!)), File.ReadAllText(RequireFile(keyPath!)),
                PasswordFromEnvironment(values, KeyPasswordEnvOption));

        values.TryGetValue(ReasonOption, out var reason);
        values.TryGetValue(LocationOption, out var location);
        values.TryGetValue(NameOption, out var name);
        values.TryGetValue(FieldOption, out var field);
        return new CliSigning(certificate, new PdfSigner.SignOptions
        {
            Reason = reason, Location = location, Name = name, FieldName = field, Appearance = Appearance(values),
        });
    }

    private static PdfSigner.SignatureAppearance? Appearance(IReadOnlyDictionary<string, string> values)
    {
        if (!values.TryGetValue(ImageOption, out var imagePath))
        {
            if (values.ContainsKey(PageOption) || values.ContainsKey(BoxOption))
                throw new ArgumentException($"{PageOption} and {BoxOption} place the {ImageOption} image; give {ImageOption} too.");
            return null;
        }
        if (!values.TryGetValue(BoxOption, out var box))
            throw new ArgumentException($"{ImageOption} needs {BoxOption} x,y,width,height (points from the page's top-left).");
        var parts = box.Split(',');
        var numbers = new double[4];
        if (parts.Length != 4) throw new ArgumentException($"{BoxOption} takes four numbers: x,y,width,height.");
        for (int i = 0; i < 4; i++)
            if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
                throw new ArgumentException($"{BoxOption} takes four numbers: x,y,width,height.");
        int page = 1;
        if (values.TryGetValue(PageOption, out var pageText) &&
            !int.TryParse(pageText, NumberStyles.Integer, CultureInfo.InvariantCulture, out page))
            throw new ArgumentException($"{PageOption} takes a page number.");
        return new PdfSigner.SignatureAppearance(page, numbers[0], numbers[1], numbers[2], numbers[3], ReadFile(imagePath));
    }

    public byte[] Sign(byte[] pdf) => PdfSigner.Sign(pdf, _certificate, _options);

    public void Dispose() => _certificate.Dispose();

    private static void RejectSigningDetailsWithoutCertificate(IReadOnlyDictionary<string, string> values)
    {
        foreach (var option in new[] { PfxPasswordEnvOption, KeyPasswordEnvOption, ReasonOption, LocationOption, NameOption, FieldOption, ImageOption, PageOption, BoxOption })
            if (values.ContainsKey(option))
                throw new ArgumentException($"{option} needs {PfxOption}, or {CertificateOption} with {KeyOption}.");
    }

    private static string? PasswordFromEnvironment(IReadOnlyDictionary<string, string> values, string option)
    {
        if (!values.TryGetValue(option, out var variable)) return null;
        return Environment.GetEnvironmentVariable(variable)
            ?? throw new ArgumentException($"{option}: environment variable '{variable}' is not set.");
    }

    private static byte[] ReadFile(string path) => File.ReadAllBytes(RequireFile(path));

    private static string RequireFile(string path) =>
        File.Exists(path) ? path : throw new ArgumentException($"File not found: {path}");
}
