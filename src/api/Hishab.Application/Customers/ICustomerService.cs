namespace Hishab.Application.Customers;

/// <summary>The current shop's customers. A phone maps to exactly one customer per shop.</summary>
public interface ICustomerService
{
    /// <summary>Customers whose name contains <paramref name="search"/>, or whose phone contains its digits; newest first.</summary>
    Task<CustomerPage> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>The quick-order auto-fill: the customer with this phone (any spelling), or null.</summary>
    Task<CustomerDto?> FindByPhoneAsync(string phone, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the customer for this phone, creating it if new. Safe under concurrency: two
    /// parallel calls for a new phone return the same customer. When the customer exists, only
    /// missing name/address are filled in.
    /// </summary>
    Task<(CustomerDto Customer, bool Created)> FindOrCreateAsync(string phone, string? name, string? address, CancellationToken cancellationToken);

    Task<CustomerDto> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<CustomerDto> UpdateAsync(Guid id, string? name, string? address, string? notes, CancellationToken cancellationToken);

    Task<CustomerDto> BlockAsync(Guid id, string? reason, CancellationToken cancellationToken);

    Task<CustomerDto> UnblockAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record CustomerDto(
    Guid Id,
    string Phone,
    string? Name,
    string? Address,
    string? Notes,
    bool IsBlocked,
    string? BlockedReason,
    DateTimeOffset? BlockedAt,
    DateTimeOffset CreatedAt);

public sealed record CustomerPage(IReadOnlyList<CustomerDto> Items, int Page, int PageSize, int TotalCount);
