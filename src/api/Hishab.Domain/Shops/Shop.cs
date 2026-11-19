namespace Hishab.Domain.Shops;

/// <summary>Staff roles within a shop (ADR 0006: one role per user, one shop per user).</summary>
public enum ShopRole
{
    Owner,
    Manager,
    Moderator,
    Packer,
}

/// <summary>
/// A seller's shop: the tenant. Its <see cref="Id"/> is the TenantId of everything the
/// shop owns. The profile (logo, pickup address, delivery charges) arrives in #7.
/// </summary>
public sealed class Shop
{
    public const string DefaultName = "My shop";
    public const int NameMaxLength = 100;

    private Shop(Guid id, string name, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Created on the owner's first successful OTP login; renamed during shop setup.</summary>
    public static Shop Create(DateTimeOffset now) => new(Guid.NewGuid(), DefaultName, now);
}
