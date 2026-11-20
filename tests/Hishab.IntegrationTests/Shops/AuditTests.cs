using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hishab.Api.Middleware;
using Hishab.Application.Common.Tenancy;
using Hishab.Infrastructure.Persistence;
using Hishab.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hishab.IntegrationTests.Shops;

public class AuditTests(HishabApiFactory factory) : IClassFixture<HishabApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly HttpClient _client = factory.CreateClient();

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, JsonElement user, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = AuthSession.Bearer(user);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await _client.SendAsync(request, Ct);
    }

    private async Task<JsonElement> Audit(JsonElement owner, string query = "")
    {
        var response = await Send(HttpMethod.Get, "/api/audit" + query, owner);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();
    }

    private static IEnumerable<JsonElement> Items(JsonElement page, string entityType, string action) =>
        page.GetProperty("items").EnumerateArray()
            .Where(i => i.GetProperty("entityType").GetString() == entityType && i.GetProperty("action").GetString() == action);

    [Fact]
    public async Task Updating_the_shop_profile_produces_one_entry_with_old_and_new_values()
    {
        var owner = await factory.SignInAsync(_client, Ct);

        var update = await Send(HttpMethod.Put, "/api/shop", owner, new
        {
            name = "Rahim Fashion",
            pickupAddress = "House 12, Road 5, Mirpur",
            deliveryChargeInsideDhaka = 70.5m,
            deliveryChargeOutsideDhaka = 120m,
        });
        var correlationId = update.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();

        var entry = Items(await Audit(owner), "Shop", "Updated").ShouldHaveSingleItem();
        var changes = entry.GetProperty("changes");
        changes.GetProperty("name").GetProperty("old").GetString().ShouldBe("My shop");
        changes.GetProperty("name").GetProperty("new").GetString().ShouldBe("Rahim Fashion");
        changes.GetProperty("deliveryChargeInsideDhaka").GetProperty("old").GetDecimal().ShouldBe(60m);
        changes.GetProperty("deliveryChargeInsideDhaka").GetProperty("new").GetDecimal().ShouldBe(70.5m);
        changes.GetProperty("pickupAddress").GetProperty("new").GetString().ShouldBe("***");
        changes.TryGetProperty("deliveryChargeOutsideDhaka", out _).ShouldBeFalse(); // unchanged fields are not recorded
        entry.GetProperty("userId").GetGuid().ShouldBe(owner.GetProperty("userId").GetGuid());
        entry.GetProperty("correlationId").GetString().ShouldBe(correlationId);
    }

    [Fact]
    public async Task Staff_role_change_records_who_changed_it()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        var staff = await factory.InviteAndSignInAsync(_client, owner, "Moderator", Ct);

        await Send(HttpMethod.Put, $"/api/staff/{staff.GetProperty("userId").GetGuid()}/role", owner, new { role = "Packer" });

        var page = await Audit(owner);
        var roleChange = Items(page, "HishabUser", "Updated").Single(i => i.GetProperty("changes").TryGetProperty("role", out _));
        roleChange.GetProperty("entityId").GetString().ShouldBe(staff.GetProperty("userId").GetString());
        roleChange.GetProperty("userId").GetGuid().ShouldBe(owner.GetProperty("userId").GetGuid());
        roleChange.GetProperty("changes").GetProperty("role").GetProperty("old").GetString().ShouldBe("Moderator");

        var invite = Items(page, "HishabUser", "Created").Single(i => i.GetProperty("entityId").GetString() == staff.GetProperty("userId").GetString());
        invite.GetProperty("changes").GetProperty("phoneNumber").GetProperty("new").GetString()!.ShouldContain("****");
        invite.GetProperty("changes").TryGetProperty("securityStamp", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Audit_log_pages_newest_first()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        for (var i = 1; i <= 3; i++)
        {
            await Send(HttpMethod.Put, "/api/shop", owner, new { name = $"Shop v{i}", deliveryChargeInsideDhaka = 60m, deliveryChargeOutsideDhaka = 120m });
        }

        var first = await Audit(owner, "?page=1&pageSize=2");
        var second = await Audit(owner, "?page=2&pageSize=2");

        first.GetProperty("totalCount").GetInt32().ShouldBe(5); // sign-up: shop + owner; then 3 updates
        first.GetProperty("items").GetArrayLength().ShouldBe(2);
        first.GetProperty("items")[0].GetProperty("changes").GetProperty("name").GetProperty("new").GetString().ShouldBe("Shop v3");
        second.GetProperty("items")[0].GetProperty("id").GetGuid().ShouldNotBe(first.GetProperty("items")[1].GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("?pageSize=101")]
    [InlineData("?page=0")]
    public async Task Invalid_paging_is_rejected(string query)
    {
        var owner = await factory.SignInAsync(_client, Ct);

        (await Send(HttpMethod.Get, "/api/audit" + query, owner)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Audit_entries_cannot_be_changed_or_deleted()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(owner.GetProperty("shopId").GetGuid());
        var db = scope.ServiceProvider.GetRequiredService<HishabDbContext>();
        var entry = await db.AuditEntries.FirstAsync(Ct);

        db.Entry(entry).Property(e => e.Changes).CurrentValue = "{}";
        await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync(Ct));

        db.ChangeTracker.Clear();
        db.AuditEntries.Remove(await db.AuditEntries.FirstAsync(Ct));
        await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync(Ct));
    }
}
