using Hishab.Domain.Common;
using Hishab.Domain.Couriers;

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
/// shop owns. Holds the profile and the delivery charges used when pricing orders.
/// </summary>
public sealed class Shop : IAuditable
{
    public const string DefaultName = "My shop";
    public const int NameMaxLength = 100;
    public const int LogoUrlMaxLength = 500;
    public const int PickupAddressMaxLength = 300;

    /// <summary>Typical courier rates, so a new shop can take orders before visiting settings.</summary>
    public static readonly Money DefaultChargeInsideDhaka = Money.Bdt(60m);
    public static readonly Money DefaultChargeOutsideDhaka = Money.Bdt(120m);

    private Shop(Guid id, string name, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        CreatedAt = createdAt;
        DeliveryChargeInsideDhaka = DefaultChargeInsideDhaka;
        DeliveryChargeOutsideDhaka = DefaultChargeOutsideDhaka;
        Plan = Plan.Free;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>An absolute http(s) URL for now; uploads come in a later issue.</summary>
    public string? LogoUrl { get; private set; }

    /// <summary>Where couriers collect parcels.</summary>
    public string? PickupAddress { get; private set; }

    public CourierProvider? DefaultCourier { get; private set; }

    public Money DeliveryChargeInsideDhaka { get; private set; }

    public Money DeliveryChargeOutsideDhaka { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The subscription plan; every shop starts on Free. Changed by billing (later), audited like any shop change.</summary>
    public Plan Plan { get; private set; }

    public PlanLimits Limits => Plans.LimitsFor(Plan);

    /// <summary>The last order number handed out; incremented atomically in the order's transaction.</summary>
    public int LastOrderNumber { get; private set; }

    public void ChangePlan(Plan plan)
    {
        _ = Plans.LimitsFor(plan); // unknown plans are rejected
        Plan = plan;
    }

    /// <summary>Created on the owner's first successful OTP login; renamed during shop setup.</summary>
    public static Shop Create(DateTimeOffset now) => new(Guid.NewGuid(), DefaultName, now);

    /// <summary>
    /// Replaces the editable profile. Enforces the invariants even though the API validates
    /// first: the entity must stay valid whoever calls it (a job, an import, a future endpoint).
    /// </summary>
    /// <exception cref="DomainValidationException">A value breaks a rule; keyed by field.</exception>
    /// <summary>Called when a courier is disconnected: a default the shop cannot use is no default.</summary>
    public void ForgetDefaultCourier(CourierProvider provider)
    {
        if (DefaultCourier == provider)
        {
            DefaultCourier = null;
        }
    }

    public void UpdateProfile(
        string name,
        string? logoUrl,
        string? pickupAddress,
        CourierProvider? defaultCourier,
        Money deliveryChargeInsideDhaka,
        Money deliveryChargeOutsideDhaka)
    {
        var errors = new Dictionary<string, string[]>();
        var trimmedName = name?.Trim() ?? string.Empty;
        logoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
        pickupAddress = string.IsNullOrWhiteSpace(pickupAddress) ? null : pickupAddress.Trim();

        if (trimmedName.Length is 0 or > NameMaxLength)
        {
            errors["name"] = [$"Shop name is required and at most {NameMaxLength} characters."];
        }

        if (logoUrl is not null && (logoUrl.Length > LogoUrlMaxLength || !IsHttpUrl(logoUrl)))
        {
            errors["logoUrl"] = [$"Logo must be an http or https URL of at most {LogoUrlMaxLength} characters."];
        }

        if (pickupAddress?.Length > PickupAddressMaxLength)
        {
            errors["pickupAddress"] = [$"Pickup address is at most {PickupAddressMaxLength} characters."];
        }

        if (deliveryChargeInsideDhaka.IsNegative)
        {
            errors["deliveryChargeInsideDhaka"] = ["Delivery charge cannot be negative."];
        }

        if (deliveryChargeOutsideDhaka.IsNegative)
        {
            errors["deliveryChargeOutsideDhaka"] = ["Delivery charge cannot be negative."];
        }

        if (errors.Count > 0)
        {
            throw new DomainValidationException(errors);
        }

        Name = trimmedName;
        LogoUrl = logoUrl;
        PickupAddress = pickupAddress;
        DefaultCourier = defaultCourier;
        DeliveryChargeInsideDhaka = deliveryChargeInsideDhaka;
        DeliveryChargeOutsideDhaka = deliveryChargeOutsideDhaka;
    }

    public static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
