using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hishab.Infrastructure.Couriers;

/// <summary>The certificate that wraps the Data Protection key ring (ADR 0007). From user-secrets or environment variables only.</summary>
public sealed class DataProtectionKeyOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>Base64 of a PFX (PKCS#12) holding an RSA certificate with its private key.</summary>
    public string CertificateBase64 { get; set; } = string.Empty;

    public string? CertificatePassword { get; set; }

    internal X509Certificate2? TryLoadCertificate()
    {
        var bytes = new byte[CertificateBase64.Length];
        if (!Convert.TryFromBase64String(CertificateBase64, bytes, out var length) || length == 0)
        {
            return null;
        }

        try
        {
            // Ephemeral: the private key stays in memory; nothing is written to the OS key store.
            var certificate = X509CertificateLoader.LoadPkcs12(
                bytes.AsSpan(0, length), CertificatePassword, X509KeyStorageFlags.EphemeralKeySet);
            using var rsa = certificate.GetRSAPrivateKey();
            return rsa is null ? null : certificate;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    internal X509Certificate2 LoadCertificate() =>
        TryLoadCertificate() ?? throw new InvalidOperationException("DataProtection:CertificateBase64 is not a usable certificate.");
}

/// <summary>Wraps new keys with the certificate before they are written to Postgres.</summary>
internal sealed class CertificateKeyEncryption(IOptions<DataProtectionKeyOptions> options, ILoggerFactory loggerFactory)
    : IConfigureOptions<KeyManagementOptions>
{
    public void Configure(KeyManagementOptions options1) =>
        options1.XmlEncryptor = new CertificateXmlEncryptor(options.Value.LoadCertificate(), loggerFactory);
}

/// <summary>
/// Lets Data Protection unwrap stored keys with our certificate instead of looking in an OS
/// certificate store (containers have none). Only the configured certificate is ever returned.
/// </summary>
internal sealed class ConfiguredCertificateResolver(IOptions<DataProtectionKeyOptions> options) : ICertificateResolver
{
    private readonly Lazy<X509Certificate2> _certificate = new(() => options.Value.LoadCertificate());

    public X509Certificate2? ResolveCertificate(string thumbprint) =>
        string.Equals(_certificate.Value.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase) ? _certificate.Value : null;
}
