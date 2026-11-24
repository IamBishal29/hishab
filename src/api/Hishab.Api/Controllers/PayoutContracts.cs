using FluentValidation;
using Hishab.Application.Finance;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Finance;

namespace Hishab.Api.Controllers;

/// <summary>One parcel on the courier's payout statement.</summary>
/// <param name="ConsignmentId">The courier's consignment ID, as on the statement.</param>
/// <param name="Collected">Cash the courier collected for this parcel, in BDT.</param>
/// <param name="DeliveryFee">Delivery charge the courier deducted, in BDT.</param>
/// <param name="CodFee">Cash-handling (COD) fee the courier deducted, in BDT.</param>
public sealed record PayoutLineRequest(string ConsignmentId, decimal Collected, decimal DeliveryFee, decimal CodFee);

/// <summary>A payout as it arrived: the transfer and the statement lines it pays for.</summary>
/// <param name="Courier">The courier that paid.</param>
/// <param name="Reference">The courier's statement or transfer ID. <example>PTH-PAY-20261124</example></param>
/// <param name="PaidOn">The day the money arrived (Dhaka date). <example>2026-11-24</example></param>
/// <param name="NetAmount">What arrived in the shop's account, in BDT. <example>4830.00</example></param>
/// <param name="Note">Optional note for the shop.</param>
/// <param name="Lines">The parcels on the statement (1-500).</param>
public sealed record RecordPayoutRequest(
    CourierProvider Courier,
    string Reference,
    DateOnly PaidOn,
    decimal NetAmount,
    string? Note,
    IReadOnlyList<PayoutLineRequest> Lines)
{
    internal RecordPayout ToCommand() => new(
        Courier,
        Reference,
        PaidOn,
        Money.Bdt(NetAmount),
        Note,
        [.. Lines.Select(l => new PayoutLineInput(l.ConsignmentId, Money.Bdt(l.Collected), Money.Bdt(l.DeliveryFee), Money.Bdt(l.CodFee)))]);
}

public sealed class RecordPayoutRequestValidator : AbstractValidator<RecordPayoutRequest>
{
    private const decimal MaxAmount = 100_000_000m;

    public RecordPayoutRequestValidator()
    {
        RuleFor(r => r.Courier).IsInEnum();
        RuleFor(r => r.Reference).NotEmpty().MaximumLength(Payout.ReferenceMaxLength);
        RuleFor(r => r.Note).MaximumLength(Payout.NoteMaxLength);
        RuleFor(r => r.NetAmount).InclusiveBetween(0m, MaxAmount).Must(Money.HasValidScale).WithMessage("Amounts can have at most 2 decimal places.");
        RuleFor(r => r.Lines).NotEmpty().Must(l => l.Count <= Payout.MaxLines).WithMessage($"A payout can list at most {Payout.MaxLines} parcels.");
        RuleForEach(r => r.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ConsignmentId).NotEmpty().MaximumLength(Shipment.ConsignmentMaxLength);
            line.RuleFor(l => l.Collected).InclusiveBetween(0m, MaxAmount).Must(Money.HasValidScale).WithMessage("Amounts can have at most 2 decimal places.");
            line.RuleFor(l => l.DeliveryFee).InclusiveBetween(0m, MaxAmount).Must(Money.HasValidScale).WithMessage("Amounts can have at most 2 decimal places.");
            line.RuleFor(l => l.CodFee).InclusiveBetween(0m, MaxAmount).Must(Money.HasValidScale).WithMessage("Amounts can have at most 2 decimal places.");
        });
    }
}

/// <summary>One statement line and what matching decided.</summary>
/// <param name="Sequence">Position on the statement, from 1.</param>
/// <param name="ConsignmentId">The courier's consignment ID.</param>
/// <param name="Collected">Cash collected for the parcel, in BDT.</param>
/// <param name="DeliveryFee">Delivery charge deducted, in BDT.</param>
/// <param name="CodFee">COD fee deducted, in BDT.</param>
/// <param name="Match">Settled, AmountDiffers (settled; see shortfall), UnknownParcel, NotDelivered, AlreadyPaid or RepeatedInPayout.</param>
/// <param name="OrderId">The parcel's order when the consignment was found.</param>
/// <param name="OrderNumber">That order's number.</param>
/// <param name="CodDue">What the order was due, in BDT, when found.</param>
/// <param name="Shortfall">Due minus collected on a settled line (positive = short-paid); null when nothing was settled.</param>
public sealed record PayoutLineDto(
    int Sequence,
    string ConsignmentId,
    decimal Collected,
    decimal DeliveryFee,
    decimal CodFee,
    PayoutLineMatch Match,
    Guid? OrderId,
    int? OrderNumber,
    decimal? CodDue,
    decimal? Shortfall);

/// <summary>A recorded payout. NetAmount = statementNet + difference; a non-zero difference is money no line explains.</summary>
/// <param name="Id">The payout's ID.</param>
/// <param name="Courier">The courier that paid.</param>
/// <param name="Reference">The courier's statement or transfer ID.</param>
/// <param name="PaidOn">The day the money arrived (Dhaka date).</param>
/// <param name="NetAmount">What arrived, in BDT.</param>
/// <param name="Collected">Sum of the lines' collected cash, in BDT.</param>
/// <param name="DeliveryFees">Sum of the lines' delivery charges, in BDT.</param>
/// <param name="CodFees">Sum of the lines' COD fees, in BDT.</param>
/// <param name="StatementNet">Collected minus delivery and COD fees, from the lines.</param>
/// <param name="Difference">NetAmount minus statementNet: negative = less arrived than the statement says.</param>
/// <param name="SettledParcels">Lines that settled an order (Settled or AmountDiffers).</param>
/// <param name="MismatchedParcels">Lines that need a look: amount differs, or nothing settled.</param>
/// <param name="Note">The shop's note.</param>
/// <param name="RecordedAt">When it was entered (UTC).</param>
public sealed record PayoutSummaryDto(
    Guid Id,
    CourierProvider Courier,
    string Reference,
    DateOnly PaidOn,
    decimal NetAmount,
    decimal Collected,
    decimal DeliveryFees,
    decimal CodFees,
    decimal StatementNet,
    decimal Difference,
    int SettledParcels,
    int MismatchedParcels,
    string? Note,
    DateTimeOffset RecordedAt)
{
    internal static PayoutSummaryDto From(Payout p) => new(
        p.Id,
        p.Provider,
        p.Reference,
        p.PaidOn,
        p.NetAmount.Amount,
        p.Collected.Amount,
        p.DeliveryFees.Amount,
        p.CodFees.Amount,
        p.StatementNet.Amount,
        p.Difference.Amount,
        p.Lines.Count(l => l.SettlesOrderId is not null),
        p.Lines.Count(l => l.Match != PayoutLineMatch.Settled),
        p.Note,
        p.RecordedAt);
}

public sealed record PayoutResponse(PayoutSummaryDto Payout, IReadOnlyList<PayoutLineDto> Lines)
{
    internal static PayoutResponse From(PayoutDetails details) => new(
        PayoutSummaryDto.From(details.Payout),
        [.. details.Payout.Lines.Select(l => new PayoutLineDto(
            l.Sequence,
            l.ConsignmentId,
            l.Collected.Amount,
            l.DeliveryFee.Amount,
            l.CodFee.Amount,
            l.Match,
            l.OrderId,
            l.OrderId is { } id && details.OrderNumbers.TryGetValue(id, out var number) ? number : null,
            l.CodDue?.Amount,
            l.Shortfall?.Amount))]);
}

public sealed record PayoutListResponse(IReadOnlyList<PayoutSummaryDto> Items, int Page, int PageSize, int Total);

public sealed record PayoutListQuery
{
    public const int MaxPageSize = 50;

    /// <summary>Only this courier's payouts.</summary>
    public CourierProvider? Courier { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

public sealed class PayoutListQueryValidator : AbstractValidator<PayoutListQuery>
{
    public PayoutListQueryValidator()
    {
        RuleFor(q => q.Courier).IsInEnum();
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PayoutListQuery.MaxPageSize);
    }
}
