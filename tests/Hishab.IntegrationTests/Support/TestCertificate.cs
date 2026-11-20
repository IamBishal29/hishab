using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Hishab.IntegrationTests.Support;

/// <summary>A throwaway self-signed RSA certificate for the Data Protection key ring (ADR 0007). Never written to disk.</summary>
internal static class TestCertificate
{
    private static readonly Lazy<string> _pfx = new(() =>
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Hishab test Data Protection", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return Convert.ToBase64String(certificate.Export(X509ContentType.Pfx));
    });

    public static string PfxBase64 => _pfx.Value;
}
