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

    /// <summary>
    /// Books one parcel. Must be idempotent on <see cref="ParcelRequest.MerchantReference"/>: booking
    /// a reference the courier already has returns that parcel (look it up if the courier answers
    /// "duplicate"), never a second one. A rejection is a result; no answer at all throws
    /// <see cref="CourierUnavailableException"/>.
    /// </summary>
    Task<BookingResult> BookAsync(CourierCredentials credentials, ParcelRequest parcel, CancellationToken cancellationToken);

    /// <summary>
    /// Most parcels the courier's bulk endpoint takes in one call, or null when it has none (then
    /// bulk booking calls <see cref="BookAsync"/> per parcel, a few at a time).
    /// </summary>
    int? MaxBulkBookingSize { get; }

    /// <summary>
    /// Books several parcels in one call (at most <see cref="MaxBulkBookingSize"/>); each result says
    /// whether its parcel was booked (partial success), matched by merchant reference. Same
    /// idempotency rule as <see cref="BookAsync"/>; no answer at all throws <see cref="CourierUnavailableException"/>.
    /// </summary>
    Task<IReadOnlyList<BookingResult>> BookManyAsync(
        CourierCredentials credentials, IReadOnlyList<ParcelRequest> parcels, CancellationToken cancellationToken);

    Task<ParcelStatusResult> GetStatusAsync(CourierCredentials credentials, string consignmentId, CancellationToken cancellationToken);
}

/// <param name="Name">Key in <see cref="CourierCredentials"/> and in the API request, e.g. "apiKey".</param>
/// <param name="Label">What the seller sees, e.g. "API key".</param>
public sealed record CourierCredentialField(string Name, string Label);

/// <summary>The seller's keys for one courier. ToString never shows them (safe if logged by mistake).</summary>
public sealed class CourierCredentials(
    IReadOnlyDictionary<string, string> values,
    Func<IReadOnlyDictionary<string, string>, CancellationToken, Task>? saveRefreshed = null)
{
    public IReadOnlyDictionary<string, string> Values { get; } = values;

    /// <summary>
    /// For adapters whose tokens rotate (OAuth refresh): saves the new values, encrypted, for this
    /// shop's connection, so the next call (or another instance) uses them. No-op when there is
    /// nothing to save to (keys being checked before they are stored).
    /// </summary>
    public Task SaveRefreshedAsync(IReadOnlyDictionary<string, string> refreshed, CancellationToken cancellationToken) =>
        saveRefreshed?.Invoke(refreshed, cancellationToken) ?? Task.CompletedTask;

    public string this[string name] =>
        Values.TryGetValue(name, out var value) ? value : throw new KeyNotFoundException($"No credential named '{name}'.");

    public override string ToString() => "CourierCredentials { *** }";
}

/// <param name="Keep">
/// What to store instead of what the seller typed, e.g. tokens obtained with a password that must
/// never be stored. Null: store the typed values.
/// </param>
public sealed record CredentialCheck(bool IsValid, string? Message, IReadOnlyDictionary<string, string>? Keep = null)
{
    public static readonly CredentialCheck Valid = new(true, null);

    public static CredentialCheck Invalid(string message) => new(false, message);

    public static CredentialCheck ValidKeeping(IReadOnlyDictionary<string, string> keep) => new(true, null, keep);
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

/// <param name="CourierStatus">The courier's own status text; required when <paramref name="Status"/> is Unknown.</param>
public sealed record ParcelStatusResult(string ConsignmentId, ParcelStatus Status, DateTimeOffset? UpdatedAt, string? CourierStatus = null);

/// <summary>Finds the adapter for a courier code (strategy selected at runtime).</summary>
public interface ICourierProviderRegistry
{
    /// <summary>Couriers with an adapter, in display order.</summary>
    IReadOnlyList<ICourierProvider> All { get; }

    ICourierProvider? Find(CourierProvider code);
}
