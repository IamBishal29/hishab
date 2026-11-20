using Hishab.Domain.Common;

namespace Hishab.Domain.Couriers;

/// <summary>Where a booking stands on our side.</summary>
public enum ShipmentState
{
    /// <summary>Saved before calling the courier: a parcel may exist there even if we never heard back.</summary>
    Requested,

    /// <summary>The courier confirmed the parcel; consignment ID and tracking code are known.</summary>
    Booked,

    /// <summary>The courier refused it (bad address, unreachable phone). Can be requested again.</summary>
    Rejected,
}

/// <summary>
/// One order sent with one courier. Created as <see cref="ShipmentState.Requested"/> and saved
/// before the courier is called (an intent record), so a crash after the courier booked the
/// parcel leaves a trace: retrying sends the same <see cref="MerchantReference"/> and the courier
/// returns the same parcel. Status sync and COD payouts attach here later, not to the order.
/// </summary>
public sealed class Shipment : ITenantOwned, IAuditable
{
    public const int ReferenceMaxLength = 64;
    public const int ConsignmentMaxLength = 64;
    public const int ErrorMaxLength = 300;

    private Shipment()
    {
        MerchantReference = null!;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; set; }

    public Guid OrderId { get; private set; }

    public CourierProvider Provider { get; private set; }

    /// <summary>The idempotency key we send to the courier: the same for every attempt on this order.</summary>
    public string MerchantReference { get; private set; }

    public ShipmentState State { get; private set; }

    public string? ConsignmentId { get; private set; }

    public string? TrackingCode { get; private set; }

    public Money? DeliveryFee { get; private set; }

    /// <summary>Why the courier refused the last attempt; shown to staff.</summary>
    public string? Error { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset? BookedAt { get; private set; }

    /// <summary>The order ID is the reference: unique everywhere, and it never changes between retries.</summary>
    public static string ReferenceFor(Guid orderId) => orderId.ToString("N");

    public static Shipment Request(Guid orderId, CourierProvider provider, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            Provider = provider,
            MerchantReference = ReferenceFor(orderId),
            State = ShipmentState.Requested,
            RequestedAt = now,
        };

    /// <summary>Try again after a rejection; a Requested shipment stays as it is (its outcome is still unknown).</summary>
    /// <exception cref="ConflictException">It is already booked.</exception>
    public void RequestAgain(DateTimeOffset now)
    {
        switch (State)
        {
            case ShipmentState.Booked:
                throw new ConflictException("This order is already booked with the courier.");
            case ShipmentState.Rejected:
                State = ShipmentState.Requested;
                Error = null;
                RequestedAt = now;
                break;
        }
    }

    public void MarkBooked(string consignmentId, string? trackingCode, Money? deliveryFee, DateTimeOffset now)
    {
        if (State != ShipmentState.Requested)
        {
            throw new ConflictException($"A {State} shipment cannot be marked booked.");
        }

        if (string.IsNullOrWhiteSpace(consignmentId) || consignmentId.Length > ConsignmentMaxLength)
        {
            throw new ArgumentException("The courier returned an invalid consignment ID.", nameof(consignmentId));
        }

        State = ShipmentState.Booked;
        ConsignmentId = consignmentId;
        TrackingCode = trackingCode;
        DeliveryFee = deliveryFee;
        BookedAt = now;
    }

    public void MarkRejected(string error)
    {
        if (State != ShipmentState.Requested)
        {
            throw new ConflictException($"A {State} shipment cannot be marked rejected.");
        }

        State = ShipmentState.Rejected;
        Error = error.Length > ErrorMaxLength ? error[..ErrorMaxLength] : error;
    }
}

/// <summary>
/// The courier did not answer (timeout, network, 5xx). The outcome is unknown, and retrying is
/// safe because the same reference is sent again (HTTP 503).
/// </summary>
public sealed class CourierUnavailableException(CourierProvider provider)
    : DomainException($"{provider} did not answer. Your order is safe; try booking again in a moment.")
{
    public CourierProvider Provider { get; } = provider;
}
