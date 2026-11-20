using Hishab.Domain.Couriers;

namespace Hishab.Application.Couriers;

/// <summary>The current shop's courier connections. Credentials go in, never come out (except to adapters).</summary>
public interface ICourierAccountService
{
    /// <summary>Every courier with an adapter, and whether this shop has connected it.</summary>
    Task<IReadOnlyList<CourierConnection>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Checks the keys with the courier, then saves them encrypted (replacing earlier keys for
    /// the same courier). A new courier counts against the plan's courier limit.
    /// </summary>
    /// <exception cref="Domain.Common.DomainValidationException">Unknown courier, missing fields, or keys the courier rejects.</exception>
    /// <exception cref="Domain.Shops.PlanLimitExceededException">The plan allows no more couriers.</exception>
    Task<CourierConnection> ConnectAsync(CourierProvider provider, IReadOnlyDictionary<string, string> credentials, CancellationToken cancellationToken);

    /// <exception cref="Domain.Common.NotFoundException">This courier is not connected.</exception>
    Task DisconnectAsync(CourierProvider provider, CancellationToken cancellationToken);

    /// <summary>Decrypted keys for an adapter call (booking, status sync). Never return them to a client.</summary>
    /// <exception cref="Domain.Common.NotFoundException">This courier is not connected.</exception>
    Task<CourierCredentials> GetCredentialsAsync(CourierProvider provider, CancellationToken cancellationToken);
}

public sealed record CourierConnection(
    CourierProvider Provider,
    bool IsConnected,
    string? CredentialHint,
    DateTimeOffset? ConnectedAt,
    IReadOnlyList<CourierCredentialField> Fields);

/// <summary>Encrypts credentials for one shop (ADR 0007). A ciphertext only decrypts for the shop it was made for.</summary>
public interface ICourierCredentialProtector
{
    string Protect(Guid tenantId, CourierCredentials credentials);

    /// <exception cref="Domain.Common.ConflictException">The ciphertext cannot be read (wrong shop, lost keys): reconnect.</exception>
    CourierCredentials Unprotect(Guid tenantId, string protectedCredentials);
}
