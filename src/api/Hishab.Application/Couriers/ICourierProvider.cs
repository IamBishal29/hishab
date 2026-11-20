using Hishab.Domain.Common;
using Hishab.Domain.Couriers;

namespace Hishab.Application.Couriers;

/// <summary>
/// One courier behind one interface (adapter pattern). Order code talks only to this; adding a
/// courier means adding one implementation and registering it, nothing else (PRD "Integrations").
/// Adapters call couriers only through their official APIs with the seller's own keys.
/// </summary>
public interface ICourierProvider
{
    CourierProvider Code { get; }

    /// <summary>The keys a seller must enter to connect this courier.</summary>
    IReadOnlyList<CourierCredentialField> CredentialFields { get; }

    /// <summary>Asks the courier whether the keys work, before they are saved.</summary>
    Task<CredentialCheck> ValidateCredentialsAsync(CourierCredentials credentials, CancellationToken cancellationToken);

    Task<CourierQuote> QuoteAsync(CourierCredentials credentials, ParcelRequest parcel, CancellationToken cancellationToken);

    /// <summary>Books one parcel. A rejection is a result, not an exception; network failures throw.</summary>
    Task<BookingResult> BookAsync(CourierCredentials credentials, ParcelRequest parcel, CancellationToken cancellationToken);

    /// <summary>Books several parcels; each result says whether its parcel was booked (partial success).</summary>
    Task<IReadOnlyList<BookingResult>> BookManyAsync(
        CourierCredentials credentials, IReadOnlyList<ParcelRequest> parcels, CancellationToken cancellationToken);

    Task<ParcelStatusResult> GetStatusAsync(CourierCredentials credentials, string consignmentId, CancellationToken cancellationToken);
}

/// <param name="Name">Key in <see cref="CourierCredentials"/> and in the API request, e.g. "apiKey".</param>
/// <param name="Label">What the seller sees, e.g. "API key".</param>
public sealed record CourierCredentialField(string Name, string Label);

/// <summary>The seller's keys for one courier. ToString never shows them (safe if logged by mistake).</summary>
public sealed class CourierCredentials(IReadOnlyDictionary<string, string> values)
{
    public IReadOnlyDictionary<string, string> Values { get; } = values;

    public string this[string name] =>
        Values.TryGetValue(name, out var value) ? value : throw new KeyNotFoundException($"No credential named '{name}'.");

    public override string ToString() => "CourierCredentials { *** }";
}

public sealed record CredentialCheck(bool IsValid, string? Message)
{
    public static readonly CredentialCheck Valid = new(true, null);

    public static CredentialCheck Invalid(string message) => new(false, message);
}

/// <summary>A parcel to book. <see cref="MerchantReference"/> is our order ID: couriers echo it back, and it makes retries safe.</summary>
public sealed record ParcelRequest(
    string MerchantReference,
    string RecipientName,
    string RecipientPhone,
    string RecipientAddress,
    bool IsInsideDhaka,
    Money CodAmount,
    int ItemCount,
    string? Note);

public sealed record CourierQuote(Money DeliveryFee, Money CodCharge);

public sealed record BookingResult(
    string MerchantReference, bool Succeeded, string? ConsignmentId, string? TrackingCode, Money? DeliveryFee, string? Error)
{
    public static BookingResult Booked(string reference, string consignmentId, string trackingCode, Money fee) =>
        new(reference, true, consignmentId, trackingCode, fee, null);

    public static BookingResult Rejected(string reference, string error) => new(reference, false, null, null, null, error);
}

public sealed record ParcelStatusResult(string ConsignmentId, ParcelStatus Status, DateTimeOffset? UpdatedAt);

/// <summary>Finds the adapter for a courier code (strategy selected at runtime).</summary>
public interface ICourierProviderRegistry
{
    /// <summary>Couriers with an adapter, in display order.</summary>
    IReadOnlyList<ICourierProvider> All { get; }

    ICourierProvider? Find(CourierProvider code);
}
