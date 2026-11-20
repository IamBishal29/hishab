using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Hishab.Application.Couriers;
using Hishab.Domain.Common;
using Microsoft.AspNetCore.DataProtection;

namespace Hishab.Infrastructure.Couriers;

/// <summary>
/// Data Protection with a purpose chain of (courier credentials, v1, shop ID): the shop ID is
/// part of the key derivation, so a ciphertext copied into another shop's row cannot be read
/// there. Key ring and its certificate wrapping: ADR 0007.
/// </summary>
internal sealed class CourierCredentialProtector(IDataProtectionProvider dataProtection) : ICourierCredentialProtector
{
    private const string Purpose = "Hishab.Couriers.Credentials";
    private const string Version = "v1";

    public string Protect(Guid tenantId, CourierCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        return ProtectorFor(tenantId).Protect(JsonSerializer.Serialize(credentials.Values));
    }

    public CourierCredentials Unprotect(Guid tenantId, string protectedCredentials)
    {
        try
        {
            var json = ProtectorFor(tenantId).Unprotect(protectedCredentials);
            return new CourierCredentials(JsonSerializer.Deserialize<Dictionary<string, string>>(json)!);
        }
        catch (CryptographicException)
        {
            // Wrong shop, revoked or lost key ring, or a corrupted value: the only fix is new keys.
            throw new ConflictException("The saved keys for this courier can no longer be read. Connect the courier again.");
        }
    }

    private IDataProtector ProtectorFor(Guid tenantId) =>
        dataProtection.CreateProtector(Purpose, Version, tenantId.ToString("N", CultureInfo.InvariantCulture));
}
