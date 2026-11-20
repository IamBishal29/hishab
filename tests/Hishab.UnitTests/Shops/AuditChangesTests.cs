using System.Text.Json;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Identity;
using Hishab.Infrastructure.Persistence;
using Hishab.Infrastructure.Persistence.Auditing;
using Hishab.Infrastructure.Persistence.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Hishab.UnitTests.Shops;

/// <summary>The audit diff, built from a real change tracker over the production model (no database).</summary>
public class AuditChangesTests
{
    private static HishabDbContext CreateContext() => new(
        new DbContextOptionsBuilder<HishabDbContext>()
            .UseNpgsql("Host=model-only") // never connects
            .UseSnakeCaseNamingConvention()
            .Options,
        new TenantContext());

    private static JsonElement Describe(HishabDbContext db, object entity)
    {
        db.ChangeTracker.DetectChanges();
        return JsonDocument.Parse(AuditChanges.Describe(db.Entry(entity))!).RootElement;
    }

    [Fact]
    public void Update_records_only_changed_fields_with_old_and_new_values_as_stored()
    {
        using var db = CreateContext();
        var shop = Shop.Create(DateTimeOffset.UtcNow);
        db.Attach(shop);

        shop.UpdateProfile("Rahim Fashion", null, null, CourierProvider.Pathao, Money.Bdt(70.5m), Shop.DefaultChargeOutsideDhaka);

        var changes = Describe(db, shop);
        changes.EnumerateObject().Select(p => p.Name).ShouldBe(["name", "defaultCourier", "deliveryChargeInsideDhaka"], ignoreOrder: true);
        changes.GetProperty("name").GetProperty("old").GetString().ShouldBe("My shop");
        changes.GetProperty("deliveryChargeInsideDhaka").GetProperty("new").GetDecimal().ShouldBe(70.5m); // Money stored as a number
        changes.GetProperty("defaultCourier").GetProperty("new").GetString().ShouldBe("Pathao"); // enum stored as text
    }

    [Fact]
    public void Personal_fields_are_masked_but_still_show_that_they_changed()
    {
        using var db = CreateContext();
        var shop = Shop.Create(DateTimeOffset.UtcNow);
        db.Attach(shop);

        shop.UpdateProfile("My shop", null, "House 12, Road 5, Mirpur", null, Shop.DefaultChargeInsideDhaka, Shop.DefaultChargeOutsideDhaka);

        var change = Describe(db, shop).GetProperty("pickupAddress");
        change.GetProperty("new").GetString().ShouldBe("***");
        change.ToString().ShouldNotContain("Mirpur");
    }

    [Fact]
    public void New_user_has_phone_masked_and_no_secrets_or_keys()
    {
        using var db = CreateContext();
        var user = new HishabUser
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            UserName = "+8801712345678",
            NormalizedUserName = "+8801712345678",
            PhoneNumber = "+8801712345678",
            Role = ShopRole.Packer,
            SecurityStamp = "secret-stamp",
            ConcurrencyStamp = "stamp",
            PasswordHash = "hash",
        };
        db.Add(user);

        var changes = Describe(db, user);

        changes.GetProperty("phoneNumber").GetProperty("new").GetString().ShouldBe("01712****78");
        changes.GetProperty("role").GetProperty("new").GetString().ShouldBe("Packer");
        var json = changes.ToString();
        json.ShouldNotContain("1712345678");
        foreach (var hidden in new[] { "id", "tenantId", "securityStamp", "concurrencyStamp", "passwordHash", "normalizedUserName" })
        {
            changes.TryGetProperty(hidden, out _).ShouldBeFalse(hidden);
        }
    }

    [Fact]
    public void Setting_a_field_to_its_current_value_records_nothing()
    {
        using var db = CreateContext();
        var shop = Shop.Create(DateTimeOffset.UtcNow);
        db.Attach(shop);

        shop.UpdateProfile(Shop.DefaultName, null, null, null, Shop.DefaultChargeInsideDhaka, Shop.DefaultChargeOutsideDhaka);
        db.ChangeTracker.DetectChanges();

        AuditChanges.Describe(db.Entry(shop)).ShouldBeNull();
    }
}
