using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Hishab.Application.Couriers;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;

namespace Hishab.Infrastructure.Couriers.Fake;

/// <summary>
/// An in-process courier for development, tests and demos (ADR 0005). It never calls a network
/// and never books a real parcel. Its behaviour is deterministic and scriptable:
/// <list type="bullet">
/// <item>the API key "invalid" is rejected when connecting;</item>
/// <item>a recipient phone ending in "0000" is rejected at booking ("recipient unreachable");</item>
/// <item>the consignment ID is derived from the merchant reference, so booking the same order
/// twice returns the same parcel (like a courier that honours idempotency);</item>
/// <item>fees follow the common Dhaka rates: 60 inside, 120 outside, +10 per extra item, 1% COD charge;</item>
/// <item>statuses start as Pending and move only when <see cref="FakeCourierControl"/> says so.</item>
/// </list>
/// </summary>
internal sealed class FakeCourierProvider(FakeCourierControl control, TimeProvider clock) : ICourierProvider
{
    public const string InvalidApiKey = "invalid";
    public const string UnreachablePhoneSuffix = "0000";

    public CourierProvider Code => CourierProvider.Fake;

    public IReadOnlyList<CourierCredentialField> CredentialFields { get; } = [new("apiKey", "API key")];

    public Task<CredentialCheck> ValidateCredentialsAsync(CourierCredentials credentials, CancellationToken cancellationToken) =>
        Task.FromResult(credentials["apiKey"] == InvalidApiKey
            ? CredentialCheck.Invalid("The courier rejected this API key. Check it in your courier account and try again.")
            : CredentialCheck.Valid);

    public Task<CourierQuote> QuoteAsync(CourierCredentials credentials, ParcelRequest parcel, CancellationToken cancellationToken) =>
        Task.FromResult(Quote(parcel));

    public Task<BookingResult> BookAsync(CourierCredentials credentials, ParcelRequest parcel, CancellationToken cancellationToken) =>
        Task.FromResult(Book(parcel));

    public Task<IReadOnlyList<BookingResult>> BookManyAsync(
        CourierCredentials credentials, IReadOnlyList<ParcelRequest> parcels, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BookingResult>>([.. parcels.Select(Book)]);

    public Task<ParcelStatusResult> GetStatusAsync(CourierCredentials credentials, string consignmentId, CancellationToken cancellationToken) =>
        Task.FromResult(control.StatusOf(consignmentId));

    private static CourierQuote Quote(ParcelRequest parcel)
    {
        var fee = (parcel.IsInsideDhaka ? 60m : 120m) + (10m * Math.Max(0, parcel.ItemCount - 1));
        var codCharge = Math.Round(parcel.CodAmount.Amount * 0.01m, 2, MidpointRounding.AwayFromZero);
        return new CourierQuote(Money.Bdt(fee), Money.Bdt(codCharge));
    }

    private BookingResult Book(ParcelRequest parcel)
    {
        if (parcel.RecipientPhone.EndsWith(UnreachablePhoneSuffix, StringComparison.Ordinal))
        {
            return BookingResult.Rejected(parcel.MerchantReference, "Fake courier: recipient unreachable.");
        }

        var consignmentId = ConsignmentIdFor(parcel.MerchantReference);
        control.Register(consignmentId, clock.GetUtcNow());
        return BookingResult.Booked(parcel.MerchantReference, consignmentId, $"FAKE{consignmentId[5..]}", Quote(parcel).DeliveryFee);
    }

    /// <summary>Same reference, same ID: "FAKE-" and 10 hex digits of its SHA-256.</summary>
    internal static string ConsignmentIdFor(string merchantReference)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(merchantReference));
        return "FAKE-" + Convert.ToHexString(hash, 0, 5);
    }
}

/// <summary>
/// Where tests and the demo move fake parcels along (Pending → PickedUp → … → Delivered).
/// Singleton, in memory: fake parcels are forgotten on restart and report Pending.
/// </summary>
public sealed class FakeCourierControl
{
    private readonly ConcurrentDictionary<string, ParcelStatusResult> _parcels = new(StringComparer.Ordinal);

    internal void Register(string consignmentId, DateTimeOffset at) =>
        _parcels.TryAdd(consignmentId, new ParcelStatusResult(consignmentId, ParcelStatus.Pending, at));

    public void SetStatus(string consignmentId, ParcelStatus status, DateTimeOffset at) =>
        _parcels[consignmentId] = new ParcelStatusResult(consignmentId, status, at);

    internal ParcelStatusResult StatusOf(string consignmentId) =>
        _parcels.TryGetValue(consignmentId, out var result)
            ? result
            : new ParcelStatusResult(consignmentId, ParcelStatus.Pending, null);
}
