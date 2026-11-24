using Hishab.Domain.Common;

namespace Hishab.Domain.Reports;

/// <summary>
/// A parcel the buyer took, counted on its Dhaka delivery day. Fees come from the courier's
/// payout when it has paid for the parcel, otherwise from the booking quote; a missing figure counts 0
/// and is reported in <see cref="ProfitMissing"/>, never guessed.
/// </summary>
/// <param name="Sales">The order total the buyer paid: lines + delivery charge (advance included).</param>
/// <param name="ProductCost">Snapshot cost of the lines that have one.</param>
/// <param name="LinesWithoutCost">Lines sold without a cost price (their cost counts 0).</param>
/// <param name="DeliveryFee">What the courier charged to deliver; null = unknown.</param>
/// <param name="CodFee">The courier's cash-handling fee; null until the payout arrives.</param>
public sealed record DeliveredSale(DateOnly Day, Money Sales, Money ProductCost, int LinesWithoutCost, Money? DeliveryFee, Money? CodFee);

/// <summary>A parcel that came back, counted on its Dhaka return day. The product is back on the shelf, so only the return charge is a cost.</summary>
/// <param name="ReturnCharge">Null = not entered yet.</param>
public sealed record ReturnedParcel(DateOnly Day, Money? ReturnCharge);

/// <summary>Real profit = sales - product cost - delivery fees - return charges - COD fees.</summary>
public sealed record ProfitFigures(
    int Delivered,
    int Returned,
    Money Sales,
    Money ProductCost,
    Money DeliveryFees,
    Money ReturnCharges,
    Money CodFees)
{
    public static ProfitFigures Zero { get; } = new(0, 0, Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero);

    public Money Profit => Sales - ProductCost - DeliveryFees - ReturnCharges - CodFees;

    internal ProfitFigures Add(DeliveredSale sale) => this with
    {
        Delivered = Delivered + 1,
        Sales = Sales + sale.Sales,
        ProductCost = ProductCost + sale.ProductCost,
        DeliveryFees = DeliveryFees + (sale.DeliveryFee ?? Money.Zero),
        CodFees = CodFees + (sale.CodFee ?? Money.Zero),
    };

    internal ProfitFigures Add(ReturnedParcel parcel) => this with
    {
        Returned = Returned + 1,
        ReturnCharges = ReturnCharges + (parcel.ReturnCharge ?? Money.Zero),
    };
}

/// <summary>
/// Figures that were not known and counted as 0, so profit may be overstated. The UI says so
/// instead of showing an exact-looking number.
/// </summary>
/// <param name="LinesWithoutCost">Sold lines without a cost price.</param>
/// <param name="ReturnsWithoutCharge">Returned parcels with no return charge entered.</param>
/// <param name="ParcelsWithoutDeliveryFee">Delivered parcels with neither a payout line nor a booking quote.</param>
/// <param name="ParcelsAwaitingPayout">Delivered parcels the courier has not paid yet: their COD fee is unknown.</param>
public sealed record ProfitMissing(int LinesWithoutCost, int ReturnsWithoutCharge, int ParcelsWithoutDeliveryFee, int ParcelsAwaitingPayout)
{
    public bool Any => LinesWithoutCost + ReturnsWithoutCharge + ParcelsWithoutDeliveryFee + ParcelsAwaitingPayout > 0;
}

public sealed record ProfitDay(DateOnly Day, ProfitFigures Figures);

/// <param name="Days">Every Dhaka day from first to last, including days with nothing (a chart needs the gaps).</param>
public sealed record ProfitReport(DateOnly From, DateOnly To, IReadOnlyList<ProfitDay> Days, ProfitFigures Total, ProfitMissing Missing)
{
    public const int MaxDays = 366;

    /// <summary>Pure: the caller supplies parcels already bucketed into Dhaka days.</summary>
    /// <exception cref="DomainValidationException">An empty or longer-than-a-year range.</exception>
    public static ProfitReport Build(DateOnly from, DateOnly to, IEnumerable<DeliveredSale> sales, IEnumerable<ReturnedParcel> returns)
    {
        if (to < from || to.DayNumber - from.DayNumber >= MaxDays)
        {
            throw new DomainValidationException("to", $"Choose a range of 1 to {MaxDays} days.");
        }

        var days = new SortedDictionary<DateOnly, ProfitFigures>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            days[day] = ProfitFigures.Zero;
        }

        int noCost = 0, noCharge = 0, noFee = 0, unpaid = 0;
        foreach (var sale in sales.Where(s => days.ContainsKey(s.Day)))
        {
            days[sale.Day] = days[sale.Day].Add(sale);
            noCost += sale.LinesWithoutCost;
            noFee += sale.DeliveryFee is null ? 1 : 0;
            unpaid += sale.CodFee is null ? 1 : 0;
        }

        foreach (var parcel in returns.Where(r => days.ContainsKey(r.Day)))
        {
            days[parcel.Day] = days[parcel.Day].Add(parcel);
            noCharge += parcel.ReturnCharge is null ? 1 : 0;
        }

        var total = days.Values.Aggregate(ProfitFigures.Zero, (sum, d) => new ProfitFigures(
            sum.Delivered + d.Delivered,
            sum.Returned + d.Returned,
            sum.Sales + d.Sales,
            sum.ProductCost + d.ProductCost,
            sum.DeliveryFees + d.DeliveryFees,
            sum.ReturnCharges + d.ReturnCharges,
            sum.CodFees + d.CodFees));

        return new ProfitReport(from, to, [.. days.Select(d => new ProfitDay(d.Key, d.Value))], total, new ProfitMissing(noCost, noCharge, noFee, unpaid));
    }
}
