using Hishab.Application.Couriers;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Infrastructure.Couriers;
using Hishab.Infrastructure.Couriers.Fake;
using Microsoft.AspNetCore.DataProtection;

namespace Hishab.UnitTests.Couriers;

public class FakeCourierProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CourierCredentials _keys = new(new Dictionary<string, string> { ["apiKey"] = "fk_test_123" });

    private readonly FakeCourierControl _control = new();
    private static readonly DateTimeOffset _deliveredAt = new(2026, 11, 21, 6, 0, 0, TimeSpan.Zero);

    private FakeCourierProvider Provider() => new(_control, TimeProvider.System);

    private static ParcelRequest Parcel(string reference = "order-1", string phone = "+8801712345678", bool insideDhaka = true, int items = 1, decimal cod = 1360m) =>
        new(reference, "Rahima Begum", phone, "House 4, Mirpur", insideDhaka, Money.Bdt(cod), items, null);

    [Fact]
    public async Task Booking_the_same_order_twice_returns_the_same_consignment()
    {
        var first = await Provider().BookAsync(_keys, Parcel("order-1"), Ct);
        var again = await Provider().BookAsync(_keys, Parcel("order-1"), Ct);
        var other = await Provider().BookAsync(_keys, Parcel("order-2"), Ct);

        first.Succeeded.ShouldBeTrue();
        first.ConsignmentId.ShouldNotBeNull().ShouldMatch("^FAKE-[0-9A-F]{10}$");
        again.ConsignmentId.ShouldBe(first.ConsignmentId);
        other.ConsignmentId.ShouldNotBe(first.ConsignmentId);
    }

    [Fact]
    public async Task Booking_is_rejected_for_a_phone_ending_in_0000()
    {
        var result = await Provider().BookAsync(_keys, Parcel(phone: "+8801712340000"), Ct);

        result.Succeeded.ShouldBeFalse();
        result.ConsignmentId.ShouldBeNull();
        result.Error.ShouldBe("Fake courier: recipient unreachable.");
    }

    [Fact]
    public async Task Bulk_booking_reports_each_parcel_on_its_own()
    {
        var results = await Provider().BookManyAsync(_keys, [Parcel("a"), Parcel("b", phone: "+8801700000000"), Parcel("c")], Ct);

        results.Select(r => (r.MerchantReference, r.Succeeded)).ShouldBe([("a", true), ("b", false), ("c", true)]);
    }

    [Theory]
    [InlineData(true, 1, 60)]
    [InlineData(false, 1, 120)]
    [InlineData(true, 3, 80)]
    public async Task Quotes_follow_area_and_item_count(bool insideDhaka, int items, decimal fee)
    {
        var quote = await Provider().QuoteAsync(_keys, Parcel(insideDhaka: insideDhaka, items: items, cod: 1360m), Ct);

        quote.DeliveryFee.ShouldBe(Money.Bdt(fee));
        quote.CodCharge.ShouldBe(Money.Bdt(13.60m)); // 1% of COD
    }

    [Fact]
    public async Task Parcels_start_pending_and_move_only_when_the_control_says_so()
    {
        var booked = await Provider().BookAsync(_keys, Parcel(), Ct);
        var id = booked.ConsignmentId!;

        (await Provider().GetStatusAsync(_keys, id, Ct)).Status.ShouldBe(ParcelStatus.Pending);
        _control.SetStatus(id, ParcelStatus.Delivered, _deliveredAt);
        var delivered = await Provider().GetStatusAsync(_keys, id, Ct);

        delivered.Status.ShouldBe(ParcelStatus.Delivered);
        delivered.UpdatedAt.ShouldBe(_deliveredAt);
        (await Provider().GetStatusAsync(_keys, "FAKE-UNKNOWN", Ct)).Status.ShouldBe(ParcelStatus.Pending);
    }

    [Fact]
    public async Task The_api_key_invalid_is_rejected_when_connecting()
    {
        var bad = new CourierCredentials(new Dictionary<string, string> { ["apiKey"] = FakeCourierProvider.InvalidApiKey });

        (await Provider().ValidateCredentialsAsync(bad, Ct)).IsValid.ShouldBeFalse();
        (await Provider().ValidateCredentialsAsync(_keys, Ct)).ShouldBe(CredentialCheck.Valid);
    }

    [Fact]
    public void Registry_finds_adapters_by_code_and_refuses_two_for_one_courier()
    {
        var registry = new CourierProviderRegistry([Provider()]);

        registry.Find(CourierProvider.Fake).ShouldNotBeNull();
        registry.Find(CourierProvider.Pathao).ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => new CourierProviderRegistry([Provider(), Provider()]));
    }

    [Theory]
    [InlineData("fk_live_7c1e9a42d0b3abcd", "••••abcd")]
    [InlineData("short_key", "••••")] // too short to show any of it
    public void Hints_show_only_the_end_of_long_keys(string key, string hint) =>
        CourierAccount.HintFor(key).ShouldBe(hint);

    [Fact]
    public void Credentials_never_print_their_values() =>
        _keys.ToString().ShouldNotContain("fk_test_123");

    [Fact]
    public void Protected_credentials_decrypt_only_for_the_shop_they_were_made_for()
    {
        var protector = new CourierCredentialProtector(new EphemeralDataProtectionProvider());
        var shopA = Guid.NewGuid();
        var ciphertext = protector.Protect(shopA, _keys);

        ciphertext.ShouldNotContain("fk_test_123");
        protector.Unprotect(shopA, ciphertext)["apiKey"].ShouldBe("fk_test_123");
        Should.Throw<ConflictException>(() => protector.Unprotect(Guid.NewGuid(), ciphertext));
    }
}
