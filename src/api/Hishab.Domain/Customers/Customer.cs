using Hishab.Domain.Common;

namespace Hishab.Domain.Customers;

/// <summary>
/// A buyer of one shop, identified in the real world by their phone (the natural key, unique
/// per shop) and in the system by <see cref="Id"/> (the surrogate key orders point at). The
/// same phone in two shops is two independent customers. Phone, name, address and notes are
/// personal data: never logged, masked in the audit log.
/// </summary>
public sealed class Customer : ITenantOwned, IAuditable
{
    public const int NameMaxLength = 100;
    public const int AddressMaxLength = 300;
    public const int NotesMaxLength = 1_000;
    public const int BlockReasonMaxLength = 200;

    private Customer() => Phone = null!;

    public Guid Id { get; private set; }

    public Guid TenantId { get; set; }

    /// <summary>Normalised E.164 (+8801XXXXXXXXX): every spelling of a number maps to one customer.</summary>
    public string Phone { get; private set; }

    public string? Name { get; private set; }

    public string? Address { get; private set; }

    public string? Notes { get; private set; }

    public bool IsBlocked { get; private set; }

    public string? BlockedReason { get; private set; }

    public DateTimeOffset? BlockedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Customer Create(Phone phone, string? name, string? address, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(phone);
        var customer = new Customer { Id = Guid.NewGuid(), Phone = phone.Value, CreatedAt = now };
        customer.UpdateDetails(name, address, notes: null);
        return customer;
    }

    /// <summary>Replaces name, address and notes (blank becomes null).</summary>
    /// <exception cref="DomainValidationException">A value is too long; keyed by field.</exception>
    public void UpdateDetails(string? name, string? address, string? notes)
    {
        name = Clean(name);
        address = Clean(address);
        notes = Clean(notes);

        var errors = new Dictionary<string, string[]>();
        if (name?.Length > NameMaxLength)
        {
            errors["name"] = [$"Name is at most {NameMaxLength} characters."];
        }

        if (address?.Length > AddressMaxLength)
        {
            errors["address"] = [$"Address is at most {AddressMaxLength} characters."];
        }

        if (notes?.Length > NotesMaxLength)
        {
            errors["notes"] = [$"Notes are at most {NotesMaxLength} characters."];
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors);
        }

        Name = name;
        Address = address;
        Notes = notes;
    }

    /// <summary>
    /// A returning customer typed into the order form: keep what the shop already knows and only
    /// fill gaps, so a hurried entry never overwrites a curated name or address.
    /// </summary>
    /// <returns>True when something was filled in.</returns>
    public bool FillMissing(string? name, string? address)
    {
        var newName = Name ?? Clean(name);
        var newAddress = Address ?? Clean(address);
        if (newName == Name && newAddress == Address)
        {
            return false;
        }

        UpdateDetails(newName, newAddress, Notes);
        return true;
    }

    /// <summary>Marks the customer as blocked; order entry warns about blocked customers.</summary>
    public void Block(string? reason, DateTimeOffset now)
    {
        reason = Clean(reason);
        if (reason?.Length > BlockReasonMaxLength)
        {
            throw new DomainValidationException("reason", $"Reason is at most {BlockReasonMaxLength} characters.");
        }

        IsBlocked = true;
        BlockedReason = reason;
        BlockedAt = now;
    }

    public void Unblock()
    {
        IsBlocked = false;
        BlockedReason = null;
        BlockedAt = null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
