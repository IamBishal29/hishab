using Hishab.Domain.Common;
using Hishab.Domain.Orders;

namespace Hishab.Domain.Finance;

/// <summary>What matching decided for one line of a courier's payout statement.</summary>
public enum PayoutLineMatch
{
    /// <summary>Paid exactly what the order is due: the order is now CodSettled.</summary>
    Settled,

    /// <summary>Paid, but not what the order is due (short or over): settled, and the gap is kept visible.</summary>
    AmountDiffers,

    /// <summary>No booked parcel of this courier has this consignment ID (typo, or another shop's parcel).</summary>
    UnknownParcel,

    /// <summary>The parcel's order is not Delivered (still in transit here, returned, ...): nothing settled.</summary>
    NotDelivered,

    /// <summary>The order was settled before (by an earlier payout): paying it again would count the cash twice.</summary>
    AlreadyPaid,

    /// <summary>The same consignment appears earlier in this statement: only the first line counts.</summary>
    RepeatedInPayout,
}

/// <summary>One row of a courier's payout statement, as the seller typed it.</summary>
/// <param name="Collected">Cash the courier collected from the buyer for this parcel.</param>
/// <param name="DeliveryFee">The courier's delivery charge deducted for this parcel.</param>
/// <param name="CodFee">The courier's cash-handling fee (usually ~1% of the COD) deducted for this parcel.</param>
public sealed record PayoutLineInput(string ConsignmentId, Money Collected, Money DeliveryFee, Money CodFee);

/// <summary>Our booked parcel behind a consignment ID of the payout's courier, and its order's state.</summary>
public sealed record PayoutParcel(Guid ShipmentId, Guid OrderId, OrderStatus OrderStatus, Money CodDue);

/// <summary>
/// The matching rules, checked in this order (the first that applies wins): a repeat in the same
/// statement, an unknown parcel, an order already settled, an order not delivered, then the amount.
/// Pure: no database, no clock; the caller looks the parcels up.
/// </summary>
public static class PayoutMatching
{
    /// <param name="parcel">Null when no booked parcel of the payout's courier has this consignment ID.</param>
    /// <param name="seenEarlier">The consignment already appeared on an earlier line of the same payout.</param>
    public static PayoutLineMatch Decide(PayoutLineInput line, PayoutParcel? parcel, bool seenEarlier) =>
        (seenEarlier, parcel) switch
        {
            (true, _) => PayoutLineMatch.RepeatedInPayout,
            (_, null) => PayoutLineMatch.UnknownParcel,
            (_, { OrderStatus: OrderStatus.CodSettled }) => PayoutLineMatch.AlreadyPaid,
            (_, { OrderStatus: not OrderStatus.Delivered }) => PayoutLineMatch.NotDelivered,
            (_, var p) when line.Collected != p.CodDue => PayoutLineMatch.AmountDiffers,
            _ => PayoutLineMatch.Settled,
        };

    /// <summary>The lines that move their order to CodSettled; every other result settles nothing.</summary>
    public static bool Settles(PayoutLineMatch match) => match is PayoutLineMatch.Settled or PayoutLineMatch.AmountDiffers;

    /// <summary>
    /// Results that can change as parcels move on: an unknown parcel may be booked later (a lost
    /// booking answer), an undelivered one may be delivered. Settled, already paid and repeated lines are final.
    /// </summary>
    public static bool CanRematch(PayoutLineMatch match) => match is PayoutLineMatch.UnknownParcel or PayoutLineMatch.NotDelivered;
}
