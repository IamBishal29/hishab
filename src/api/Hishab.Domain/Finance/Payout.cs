using Hishab.Domain.Common;
using Hishab.Domain.Couriers;

namespace Hishab.Domain.Finance;

/// <summary>
/// One transfer from a courier to the shop, with the statement lines it pays for. The books must
/// balance: <see cref="NetAmount"/> (what arrived) = collected - delivery fees - COD fees +
/// <see cref="Difference"/>. Difference is the part no line explains; it is stored and shown, never
/// rounded away. Lines are matched once, when the payout is recorded (re-matching: #76).
/// </summary>
public sealed class Payout : ITenantOwned, IAuditable
{
    public const int ReferenceMaxLength = 64;
    public const int NoteMaxLength = 300;
    public const int MaxLines = 500;

    private readonly List<PayoutLine> _lines = [];

    private Payout()
    {
        Reference = null!;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; set; }

    public CourierProvider Provider { get; private set; }

    /// <summary>The courier's statement or transfer ID; one payout per courier and reference.</summary>
    public string Reference { get; private set; }

    /// <summary>The Dhaka day the money arrived.</summary>
    public DateOnly PaidOn { get; private set; }

    /// <summary>What actually arrived in the shop's account.</summary>
    public Money NetAmount { get; private set; }

    public string? Note { get; private set; }

    public Guid? RecordedBy { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public IReadOnlyList<PayoutLine> Lines => [.. _lines.OrderBy(l => l.Sequence)];

    public Money Collected => Sum(l => l.Collected);

    public Money DeliveryFees => Sum(l => l.DeliveryFee);

    public Money CodFees => Sum(l => l.CodFee);

    /// <summary>What the statement lines say should have arrived.</summary>
    public Money StatementNet => Collected - DeliveryFees - CodFees;

    /// <summary>Arrived minus explained: positive = more than the lines explain, negative = missing money.</summary>
    public Money Difference => NetAmount - StatementNet;

    /// <summary>
    /// Records a payout and matches every line (see <see cref="PayoutMatching"/>). The caller then
    /// moves each <see cref="PayoutLine.SettlesOrderId"/> to CodSettled in the same SaveChanges.
    /// </summary>
    /// <param name="parcels">Booked parcels of <paramref name="provider"/> by consignment ID; missing = unknown.</param>
    /// <exception cref="DomainValidationException">A field breaks a rule (keys match the request fields).</exception>
    public static Payout Record(
        CourierProvider provider,
        string reference,
        DateOnly paidOn,
        Money netAmount,
        string? note,
        IReadOnlyList<PayoutLineInput> lines,
        IReadOnlyDictionary<string, PayoutParcel> parcels,
        Guid? by,
        DateTimeOffset now)
    {
        reference = reference?.Trim() ?? "";
        Require(reference.Length is > 0 and <= ReferenceMaxLength, "reference", $"Enter the courier's payout reference (at most {ReferenceMaxLength} characters).");
        Require(paidOn <= DhakaTime.DayOf(now), "paidOn", "The payout date cannot be in the future.");
        Require(!netAmount.IsNegative, "netAmount", "The amount received cannot be negative.");
        Require(note is null || note.Length <= NoteMaxLength, "note", $"The note can have at most {NoteMaxLength} characters.");
        Require(lines.Count is > 0 and <= MaxLines, "lines", $"A payout needs between 1 and {MaxLines} parcels.");

        var payout = new Payout
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            Reference = reference,
            PaidOn = paidOn,
            NetAmount = netAmount,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            RecordedBy = by,
            RecordedAt = now,
        };

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var consignment = line.ConsignmentId?.Trim() ?? "";
            Require(consignment.Length is > 0 and <= Shipment.ConsignmentMaxLength, $"lines[{i}].consignmentId", "Enter the consignment ID.");
            Require(!line.Collected.IsNegative, $"lines[{i}].collected", "Amounts cannot be negative.");
            Require(!line.DeliveryFee.IsNegative, $"lines[{i}].deliveryFee", "Amounts cannot be negative.");
            Require(!line.CodFee.IsNegative, $"lines[{i}].codFee", "Amounts cannot be negative.");

            var parcel = parcels.GetValueOrDefault(consignment);
            var match = PayoutMatching.Decide(line, parcel, seenEarlier: !seen.Add(consignment));
            payout._lines.Add(new PayoutLine(i + 1, line with { ConsignmentId = consignment }, match, parcel));
        }

        return payout;
    }

    private Money Sum(Func<PayoutLine, Money> amount) => _lines.Aggregate(Money.Zero, (sum, l) => sum + amount(l));

    private static void Require(bool rule, string field, string message)
    {
        if (!rule)
        {
            throw new DomainValidationException(field, message);
        }
    }
}

/// <summary>One parcel on a payout statement and what matching decided for it.</summary>
public sealed class PayoutLine
{
    private PayoutLine()
    {
        ConsignmentId = null!;
    }

    internal PayoutLine(int sequence, PayoutLineInput input, PayoutLineMatch match, PayoutParcel? parcel)
    {
        Id = Guid.NewGuid();
        Sequence = sequence;
        ConsignmentId = input.ConsignmentId;
        Collected = input.Collected;
        DeliveryFee = input.DeliveryFee;
        CodFee = input.CodFee;
        Match = match;
        OrderId = parcel?.OrderId;
        CodDue = parcel?.CodDue;
        // Only a settling line claims the shipment: a unique index on this column means no parcel is
        // ever settled by two payouts, even when two are recorded at the same moment.
        SettledShipmentId = PayoutMatching.Settles(match) ? parcel!.ShipmentId : null;
    }

    public Guid Id { get; private set; }

    /// <summary>Position on the statement (1-based), so the list reads like the courier's paper.</summary>
    public int Sequence { get; private set; }

    public string ConsignmentId { get; private set; }

    public Money Collected { get; private set; }

    public Money DeliveryFee { get; private set; }

    public Money CodFee { get; private set; }

    public PayoutLineMatch Match { get; private set; }

    /// <summary>The parcel's order, when the consignment was found (also for lines that settle nothing).</summary>
    public Guid? OrderId { get; private set; }

    /// <summary>The order's COD due when matched; with <see cref="Collected"/> it shows a short or over payment.</summary>
    public Money? CodDue { get; private set; }

    public Guid? SettledShipmentId { get; private set; }

    /// <summary>The order this line moves to CodSettled, or null when it settles nothing.</summary>
    public Guid? SettlesOrderId => SettledShipmentId is null ? null : OrderId;

    /// <summary>Due minus collected on a settled line: positive = short-paid. Null when nothing was settled.</summary>
    public Money? Shortfall => SettledShipmentId is null || CodDue is null ? null : CodDue.Value - Collected;
}
