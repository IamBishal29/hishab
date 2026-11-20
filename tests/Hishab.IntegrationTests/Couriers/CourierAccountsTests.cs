using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hishab.Application.Common.Tenancy;
using Hishab.Application.Couriers;
using Hishab.Domain.Common;
using Hishab.Domain.Couriers;
using Hishab.Domain.Shops;
using Hishab.Infrastructure.Persistence;
using Hishab.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hishab.IntegrationTests.Couriers;

/// <summary>The real app plus a stub second courier, so plan limits on couriers can be tested before Steadfast exists.</summary>
public sealed class TwoCourierApiFactory : HishabApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddSingleton<ICourierProvider, StubSteadfast>());
    }

    private sealed class StubSteadfast : ICourierProvider
    {
        public CourierProvider Code => CourierProvider.Steadfast;

        public IReadOnlyList<CourierCredentialField> CredentialFields { get; } = [new("apiKey", "API key"), new("secretKey", "Secret key")];

        public Task<CredentialCheck> ValidateCredentialsAsync(CourierCredentials credentials, CancellationToken cancellationToken) =>
            Task.FromResult(CredentialCheck.Valid);

        public Task<CourierQuote> QuoteAsync(CourierCredentials credentials, ParcelRequest parcel, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BookingResult> BookAsync(CourierCredentials credentials, ParcelRequest parcel, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<BookingResult>> BookManyAsync(
            CourierCredentials credentials, IReadOnlyList<ParcelRequest> parcels, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ParcelStatusResult> GetStatusAsync(CourierCredentials credentials, string consignmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

public class CourierAccountsTests(TwoCourierApiFactory factory) : IClassFixture<TwoCourierApiFactory>
{
    private const string FakeKey = "fk_live_7c1e9a42d0b3abcd";

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

    private Task<HttpResponseMessage> Connect(JsonElement user, string provider, object credentials) =>
        Send(HttpMethod.Put, $"/api/couriers/{provider}", user, new { credentials });

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.Clone();

    private async Task<JsonElement> Courier(JsonElement user, string provider)
    {
        var list = await Json(await Send(HttpMethod.Get, "/api/couriers", user));
        return list.EnumerateArray().Single(c => c.GetProperty("provider").GetString() == provider);
    }

    private async Task<T> InShop<T>(JsonElement user, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContext>().SetTenant(user.GetProperty("shopId").GetGuid());
        return await action(scope.ServiceProvider);
    }

    [Fact]
    public async Task Connected_keys_are_stored_encrypted_and_never_returned()
    {
        var owner = await factory.SignInAsync(_client, Ct);

        var response = await Connect(owner, "Fake", new { apiKey = FakeKey });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldNotContain(FakeKey);
        var fake = await Courier(owner, "Fake");
        fake.GetProperty("isConnected").GetBoolean().ShouldBeTrue();
        fake.GetProperty("credentialHint").GetString().ShouldBe("••••abcd");
        fake.GetProperty("fields")[0].GetProperty("name").GetString().ShouldBe("apiKey");

        var (stored, audit, keyRing) = await InShop(owner, async sp =>
        {
            var db = sp.GetRequiredService<HishabDbContext>();
            var account = await db.CourierAccounts.SingleAsync(Ct);
            var entries = await db.AuditEntries.Where(e => e.EntityId == account.Id.ToString()).Select(e => e.Changes).ToListAsync(Ct);
            var keys = await db.DataProtectionKeys.Select(k => k.Xml).ToListAsync(Ct);
            return (account.ProtectedCredentials, string.Join('\n', entries), string.Join('\n', keys));
        });
        stored.ShouldNotContain(FakeKey); // ciphertext only
        audit.ShouldNotBeEmpty();
        audit.ShouldNotContain(FakeKey);
        audit.ShouldNotContain(stored); // the ciphertext stays out of the audit log too
        keyRing.ShouldContain("EncryptedXmlDecryptor"); // the key ring is encrypted XML...
        keyRing.ShouldContain("<X509Certificate>"); // ...wrapped by our certificate
        keyRing.ShouldNotContain("<masterKey"); // no plaintext master key in the database

        var decrypted = await InShop(owner, sp => sp.GetRequiredService<ICourierAccountService>().GetCredentialsAsync(CourierProvider.Fake, Ct));
        decrypted["apiKey"].ShouldBe(FakeKey); // and the adapter can still read it
    }

    [Fact]
    public async Task A_ciphertext_copied_into_another_shop_cannot_be_read_there()
    {
        var shopA = await factory.SignInAsync(_client, Ct);
        var shopB = await factory.SignInAsync(_client, Ct);
        await Connect(shopA, "Fake", new { apiKey = FakeKey });
        await Connect(shopB, "Fake", new { apiKey = "fk_live_bbbbbbbbbbbbbbbb" });

        var stolen = await InShop(shopA, async sp => await sp.GetRequiredService<HishabDbContext>().CourierAccounts.Select(a => a.ProtectedCredentials).SingleAsync(Ct));
        await InShop(shopB, async sp =>
        {
            var db = sp.GetRequiredService<HishabDbContext>();
            await db.CourierAccounts.ExecuteUpdateAsync(s => s.SetProperty(a => a.ProtectedCredentials, stolen), Ct); // a tenant-mixing bug, simulated
            return 0;
        });

        await Should.ThrowAsync<ConflictException>(() =>
            InShop(shopB, sp => sp.GetRequiredService<ICourierAccountService>().GetCredentialsAsync(CourierProvider.Fake, Ct)));
    }

    [Fact]
    public async Task Keys_the_courier_rejects_or_incomplete_keys_are_not_saved()
    {
        var owner = await factory.SignInAsync(_client, Ct);

        var rejected = await Connect(owner, "Fake", new { apiKey = "invalid" });
        var missing = await Connect(owner, "Steadfast", new { apiKey = "sf_key_1234567890" });
        var unknown = await Connect(owner, "Pathao", new { apiKey = "x" });

        rejected.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(rejected)).GetProperty("errors").GetProperty("credentials")[0].GetString()!.ShouldContain("rejected this API key");
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(missing)).GetProperty("errors").TryGetProperty("credentials.secretKey", out _).ShouldBeTrue();
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Json(unknown)).GetProperty("errors").GetProperty("provider")[0].GetString().ShouldBe("Pathao is not available yet.");
        (await Courier(owner, "Fake")).GetProperty("isConnected").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Free_plan_connects_one_courier_but_can_always_replace_its_keys()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        (await Connect(owner, "Fake", new { apiKey = FakeKey })).StatusCode.ShouldBe(HttpStatusCode.OK);

        var second = await Connect(owner, "Steadfast", new { apiKey = "sf_key_1234567890", secretKey = "sf_secret_1234567890" });
        var rotated = await Connect(owner, "Fake", new { apiKey = "fk_live_rotated_00009999" });

        second.StatusCode.ShouldBe(HttpStatusCode.PaymentRequired);
        (await Json(second)).GetProperty("detail").GetString().ShouldBe("Your Free plan allows 1 courier. Upgrade your plan to add more.");
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await Courier(owner, "Fake")).GetProperty("credentialHint").GetString().ShouldBe("••••9999");

        await factory.ChangePlanAsync(owner, Plan.Starter, Ct);
        (await Connect(owner, "Steadfast", new { apiKey = "sf_key_1234567890", secretKey = "sf_secret_1234567890" })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await factory.ChangePlanAsync(owner, Plan.Free, Ct); // downgraded with two couriers: rotating a key still works
        (await Connect(owner, "Steadfast", new { apiKey = "sf_key_rotated_7777", secretKey = "sf_secret_1234567890" })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Two_parallel_connects_on_the_free_plan_cannot_both_pass()
    {
        var owner = await factory.SignInAsync(_client, Ct);

        var results = await Task.WhenAll(
            Connect(owner, "Fake", new { apiKey = FakeKey }),
            Connect(owner, "Steadfast", new { apiKey = "sf_key_1234567890", secretKey = "sf_secret_1234567890" }));

        results.Select(r => r.StatusCode).Order().ShouldBe([HttpStatusCode.OK, HttpStatusCode.PaymentRequired]);
    }

    [Fact]
    public async Task Disconnecting_deletes_the_keys()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        await Connect(owner, "Fake", new { apiKey = FakeKey });

        (await Send(HttpMethod.Delete, "/api/couriers/Fake", owner)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Courier(owner, "Fake")).GetProperty("isConnected").GetBoolean().ShouldBeFalse();
        (await InShop(owner, sp => sp.GetRequiredService<HishabDbContext>().CourierAccounts.CountAsync(Ct))).ShouldBe(0);
        (await Send(HttpMethod.Delete, "/api/couriers/Fake", owner)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Owners_and_managers_connect_couriers_and_all_staff_see_them()
    {
        var owner = await factory.SignInAsync(_client, Ct);
        var manager = await factory.InviteAndSignInAsync(_client, owner, "Manager", Ct);
        var moderator = await factory.InviteAndSignInAsync(_client, owner, "Moderator", Ct);
        var packer = await factory.InviteAndSignInAsync(_client, owner, "Packer", Ct);

        (await Connect(manager, "Fake", new { apiKey = FakeKey })).StatusCode.ShouldBe(HttpStatusCode.OK); // ManageShop
        (await Connect(moderator, "Fake", new { apiKey = FakeKey })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Delete, "/api/couriers/Fake", packer)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Send(HttpMethod.Get, "/api/couriers", packer)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
