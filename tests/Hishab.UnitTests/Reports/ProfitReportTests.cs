using Hishab.Domain.Common;
using Hishab.Domain.Reports;

namespace Hishab.UnitTests.Reports;

public class ProfitReportTests
{
    private static readonly DateOnly _day1 = new(2026, 11, 1);
    private static readonly DateOnly _day2 = new(2026, 11, 2);

    private static DeliveredSale Sale(DateOnly day, decimal sales, decimal cost, decimal? deliveryFee = 60m, decimal? codFee = 10m, int linesWithoutCost = 0) =>
        new(day, Money.Bdt(sales), Money.Bdt(cost), linesWithoutCost,
            deliveryFee is null ? null : Money.Bdt(deliveryFee.Value), codFee is null ? null : Money.Bdt(codFee.Value));

    private static ReturnedParcel Return(DateOnly day, decimal? charge) => new(day, charge is null ? null : Money.Bdt(charge.Value));

    [Fact]
    public void Profit_is_sales_minus_cost_delivery_returns_and_cod_fees()
    {
        var report = ProfitReport.Build(_day1, _day1, [Sale(_day1, 1060m, 600m)], [Return(_day1, 60m)]);

        var total = report.Total;
        (total.Sales, total.ProductCost, total.DeliveryFees, total.ReturnCharges, total.CodFees)
            .ShouldBe((Money.Bdt(1060m), Money.Bdt(600m), Money.Bdt(60m), Money.Bdt(60m), Money.Bdt(10m)));
        total.Profit.ShouldBe(Money.Bdt(330m)); // 1060 - 600 - 60 - 60 - 10
        (total.Delivered, total.Returned).ShouldBe((1, 1));
    }

    [Fact]
    public void Returns_can_make_a_day_a_loss()
    {
        var report = ProfitReport.Build(_day1, _day1, [], [Return(_day1, 60m), Return(_day1, 120m)]);

        report.Total.Profit.ShouldBe(Money.Bdt(-180m));
    }

    [Fact]
    public void Every_day_in_the_range_is_listed_and_parcels_land_on_their_own_day()
    {
        var report = ProfitReport.Build(_day1, _day1.AddDays(2), [Sale(_day2, 500m, 300m, 60m, 5m)], []);

        report.Days.Select(d => d.Day).ShouldBe([_day1, _day2, _day1.AddDays(2)]);
        report.Days.Select(d => d.Figures.Profit.Amount).ShouldBe([0m, 135m, 0m]);
    }

    [Fact]
    public void Parcels_outside_the_range_are_ignored()
    {
        var report = ProfitReport.Build(_day2, _day2, [Sale(_day1, 500m, 300m)], [Return(_day1.AddDays(5), 60m)]);

        report.Total.ShouldBe(ProfitFigures.Zero);
    }

    [Fact]
    public void Unknown_figures_count_zero_and_are_reported_as_missing()
    {
        var report = ProfitReport.Build(
            _day1,
            _day1,
            [Sale(_day1, 1000m, 400m, deliveryFee: null, codFee: null, linesWithoutCost: 2), Sale(_day1, 500m, 200m)],
            [Return(_day1, null)]);

        report.Missing.ShouldBe(new ProfitMissing(LinesWithoutCost: 2, ReturnsWithoutCharge: 1, ParcelsWithoutDeliveryFee: 1, ParcelsAwaitingPayout: 1));
        report.Missing.Any.ShouldBeTrue();
        report.Total.Profit.ShouldBe(Money.Bdt(1500m - 600m - 60m - 10m));
    }

    [Fact]
    public void A_complete_month_has_nothing_missing()
    {
        ProfitReport.Build(_day1, _day2, [Sale(_day1, 500m, 200m)], [Return(_day2, 60m)]).Missing.Any.ShouldBeFalse();
    }

    [Theory]
    [InlineData(1, 0)]     // ends before it starts
    [InlineData(0, 366)]   // longer than a year
    public void Ranges_must_be_one_day_to_a_year(int startOffset, int endOffset)
    {
        Should.Throw<DomainValidationException>(() => ProfitReport.Build(_day1.AddDays(startOffset), _day1.AddDays(endOffset), [], []));
    }
}
