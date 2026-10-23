using System;
using System.Collections.Generic;
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
        return new CliSigning(certificate, new PdfSigner.SignOptions { Reason = reason, Location = location, Name = name });
    }

    public byte[] Sign(byte[] pdf) => PdfSigner.Sign(pdf, _certificate, _options);

    public void Dispose() => _certificate.Dispose();

    private static void RejectSigningDetailsWithoutCertificate(IReadOnlyDictionary<string, string> values)
    {
        foreach (var option in new[] { PfxPasswordEnvOption, KeyPasswordEnvOption, ReasonOption, LocationOption, NameOption })
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
