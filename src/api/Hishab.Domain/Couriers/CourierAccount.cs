using Hishab.Domain.Common;

namespace Hishab.Domain.Couriers;

/// <summary>
/// A shop's connection to one courier with the seller's own API keys (one per shop and
/// courier). The keys are stored only encrypted (<see cref="ProtectedCredentials"/>, ADR 0007)
/// and never leave the API; <see cref="CredentialHint"/> is what people see.
/// </summary>
public sealed class CourierAccount : ITenantOwned, IAuditable
{
    private CourierAccount()
    {
        ProtectedCredentials = null!;
        CredentialHint = null!;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; set; }

    public CourierProvider Provider { get; private set; }

    /// <summary>Ciphertext of the credentials, bound to this shop. Never logged, audited or returned.</summary>
    public string ProtectedCredentials { get; private set; }

    /// <summary>Masked key for display, e.g. "••••a9f2".</summary>
    public string CredentialHint { get; private set; }

    public DateTimeOffset ConnectedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static CourierAccount Connect(CourierProvider provider, string protectedCredentials, string hint, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            ProtectedCredentials = protectedCredentials,
            CredentialHint = hint,
            ConnectedAt = now,
            UpdatedAt = now,
        };

    /// <summary>New keys for the same courier (the seller rotated them).</summary>
    public void ReplaceCredentials(string protectedCredentials, string hint, DateTimeOffset now)
    {
        ProtectedCredentials = protectedCredentials;
        CredentialHint = hint;
        UpdatedAt = now;
    }

    /// <summary>The courier rotated our tokens (OAuth refresh): same connection, new ciphertext, same hint.</summary>
    public void RotateCredentials(string protectedCredentials, DateTimeOffset now)
    {
        ProtectedCredentials = protectedCredentials;
        UpdatedAt = now;
    }

    /// <summary>Shows only the end of a key, and nothing at all of a short one.</summary>
    public static string HintFor(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return secret.Length >= 12 ? $"••••{secret[^4..]}" : "••••";
    }
}
